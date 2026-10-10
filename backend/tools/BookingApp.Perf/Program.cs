// Seeds a PostgreSQL database with a year of realistic bookings and measures the hot booking queries: their plans
// (EXPLAIN ANALYZE, BUFFERS) and timings over many varied calls. Library: 12.4 "Example 2: Load testing with realistic
// data" (volume and distribution, not an empty table), 7.3 "Reading a query plan", 7.9 Case 1 (rows read vs returned).
//
//   dotnet run -c Release --project tools/BookingApp.Perf -- seed      [--connection <PostgreSQL connection string>]
//   dotnet run -c Release --project tools/BookingApp.Perf -- migrate   [--connection ...]
//   dotnet run -c Release --project tools/BookingApp.Perf -- measure   --label before|after [--connection ...]
//
// The default connection is the docker-compose PostgreSQL, database booking_perf (seed creates it, dropping any old one).
using System.Diagnostics;
using System.Globalization;
using System.Text;
using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

var command = args.FirstOrDefault() ?? "help";
string Option(string name, string fallback)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
}
var connection = Option("--connection", "Host=localhost;Port=5432;Database=booking_perf;Username=booking;Password=booking-dev-only");

switch (command)
{
    case "seed":
        await Seeder.RunAsync(connection);
        break;
    case "migrate":
        await using (var db = Perf.Context(connection)) await db.Database.MigrateAsync();
        Console.WriteLine("Migrated.");
        break;
    case "measure":
        await Perf.MeasureAsync(connection, Option("--label", "now"));
        break;
    default:
        Console.WriteLine("seed | migrate | measure --label <name>   [--connection <PostgreSQL connection string>]");
        break;
}

internal static class Perf
{
    public static readonly Venue Venue = new("Perf club", "Asia/Ho_Chi_Minh");

    public static PostgresBookingDbContext Context(string connection) =>
        new(new DbContextOptionsBuilder<PostgresBookingDbContext>().UseNpgsql(connection).Options);

    /// <summary>The window availability asks for: one venue-local day.</summary>
    public static TimeRange Day(DateOnly day) => AvailabilityCalculator.DayWindow(Venue, day);

    public static async Task MeasureAsync(string connection, string label)
    {
        var report = new StringBuilder();
        void Line(string text = "")
        {
            Console.WriteLine(text);
            report.AppendLine(text);
        }

        await using var db = Context(connection);
        var resources = await db.Resources.Select(r => r.Id).OrderBy(id => id).ToListAsync();
        var heavy = await db.Bookings.GroupBy(b => b.CustomerId).OrderByDescending(g => g.Count()).Select(g => g.Key).Take(10).ToListAsync();
        var regular = await db.Bookings.GroupBy(b => b.CustomerId).Where(g => g.Count() < 100).Select(g => g.Key).Take(500).ToListAsync();
        var total = await db.Bookings.CountAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Line($"## {label}: {total:N0} bookings, {resources.Count} resources");
        Line();

        // A day late in the season: with an unbounded overlap predicate, the worst case.
        var lateDay = today.AddDays(60);
        await PlanAsync("Availability (one resource, one day)", BookingQueries.Blocking(db, resources[0], Day(lateDay)));
        await PlanAsync("My bookings (a regular customer)", BookingQueries.ForCustomer(db, regular[0]));
        await PlanAsync("My bookings (a heavy customer)", BookingQueries.ForCustomer(db, heavy[0]));
        await PlanAsync("Staff schedule (every resource, one day)", BookingQueries.InWindow(db, Day(lateDay)));

        var random = new Random(42);
        Line("| Query | calls | p50 ms | p95 ms | max ms |");
        Line("|---|---:|---:|---:|---:|");
        await TimeAsync("Availability, random resource and day", 400,
            () => BookingQueries.Blocking(db, resources[random.Next(resources.Count)], Day(today.AddDays(random.Next(-270, 90)))).ToListAsync());
        await TimeAsync("My bookings, regular customer", 400, () => BookingQueries.ForCustomer(db, regular[random.Next(regular.Count)]).ToListAsync());
        await TimeAsync("My bookings, heavy customer", 100, () => BookingQueries.ForCustomer(db, heavy[random.Next(heavy.Count)]).ToListAsync());
        await TimeAsync("Staff schedule, random day", 100, () => BookingQueries.InWindow(db, Day(today.AddDays(random.Next(-270, 90)))).ToListAsync());
        Line();

        var path = Path.Combine(AppContext.BaseDirectory, $"perf-{label}.md");
        await File.WriteAllTextAsync(path, report.ToString());
        Console.WriteLine($"Written to {path}");

        async Task PlanAsync(string title, IQueryable<BookingRow> query)
        {
            await using var cmd = query.CreateDbCommand();
            cmd.CommandText = "EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF, SUMMARY ON) " + cmd.CommandText;
            await db.Database.OpenConnectionAsync();
            Line($"### {title}");
            Line("```text");
            await using (var reader = await cmd.ExecuteReaderAsync())
                while (await reader.ReadAsync()) Line(reader.GetString(0));
            Line("```");
            Line();
        }

        async Task TimeAsync<T>(string title, int calls, Func<Task<T>> query)
        {
            for (var i = 0; i < 20; i++) await query();   // warm up: plans cached, pages in memory
            var times = new List<double>();
            for (var i = 0; i < calls; i++)
            {
                var watch = Stopwatch.StartNew();
                await query();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            string Ms(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            Line($"| {title} | {calls} | {Ms(times[calls / 2])} | {Ms(times[(int)(calls * 0.95)])} | {Ms(times[^1])} |");
        }
    }
}

/// <summary>
/// A year of bookings shaped like the real thing: 30 courts open 06:00–22:00 every day, 270 days of history and 95 days
/// ahead, busy in the past and thinning out into the future; most customers book a few times, a handful (coaches) book
/// every week; past bookings are completed, cancelled or no-shows. Written with binary COPY, then ANALYZEd.
/// </summary>
internal static class Seeder
{
    public static async Task RunAsync(string connection)
    {
        var builder = new NpgsqlConnectionStringBuilder(connection);
        var database = builder.Database!;
        builder.Database = "postgres";
        await using (var admin = new NpgsqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE); ", admin);
            await drop.ExecuteNonQueryAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\";", admin);
            await create.ExecuteNonQueryAsync();
        }
        await using (var db = Perf.Context(connection)) await db.Database.MigrateAsync();

        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
        var watch = Stopwatch.StartNew();
        await Sql(conn, "INSERT INTO venue (id, name, time_zone_id, max_days_ahead, min_lead_minutes, cancellation_cutoff_hours, max_active_bookings) " +
            "VALUES (1, 'Perf club', 'Asia/Ho_Chi_Minh', 30, 15, 2, 5)");
        var resources = Enumerable.Range(1, 30).Select(i => $"court-{i:00}").ToList();
        foreach (var (id, i) in resources.Select((id, i) => (id, i)))
        {
            await Sql(conn, $"INSERT INTO resources (id, name, kind, capacity, slot_minutes, max_slots_per_booking, sort_order) VALUES ('{id}', 'Court {i + 1}', 'Court', 4, 60, 2, {i})");
            for (var day = 0; day < 7; day++)
                await Sql(conn, $"INSERT INTO opening_hours (resource_id, day_of_week, open_time, close_time) VALUES ('{id}', {day}, '06:00', '22:00')");
        }

        var random = new Random(7);
        var regulars = Enumerable.Range(0, 4000).Select(_ => Guid.CreateVersion7()).ToArray();
        var coaches = Enumerable.Range(0, 10).Select(_ => Guid.CreateVersion7()).ToArray();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTimeOffset.UtcNow;
        long rows = 0;

        await using (var copy = await conn.BeginBinaryImportAsync(
            "COPY bookings (id, resource_id, start_utc, end_utc, customer_id, customer_name, status, created_at, cancelled_at, version) FROM STDIN (FORMAT BINARY)"))
        {
            for (var offset = -270; offset < 95; offset++)
            {
                var day = today.AddDays(offset);
                // Busy in the past (75%), filling up near today, sparse far ahead.
                var occupancy = offset < 0 ? 0.75 : Math.Max(0.1, 0.7 - offset * 0.007);
                foreach (var resource in resources)
                {
                    for (var hour = 6; hour < 22; hour++)
                    {
                        if (random.NextDouble() > occupancy) continue;
                        var start = AvailabilityCalculator.ToUtc(day.ToDateTime(new TimeOnly(hour, 0)), Perf.Venue);
                        var end = start.AddHours(1);
                        var customer = random.NextDouble() < 0.15 ? coaches[random.Next(coaches.Length)] : regulars[(int)(Math.Pow(random.NextDouble(), 2) * regulars.Length)];
                        var roll = random.NextDouble();
                        var status = offset < 0
                            ? roll < 0.12 ? "Cancelled" : roll < 0.15 ? "NoShow" : "Completed"
                            : roll < 0.12 ? "Cancelled" : "Confirmed";
                        var created = start.AddDays(-random.Next(1, 30));
                        if (created > now) created = now;
                        await copy.StartRowAsync();
                        await copy.WriteAsync(Guid.CreateVersion7(), NpgsqlDbType.Uuid);
                        await copy.WriteAsync(resource, NpgsqlDbType.Varchar);
                        await copy.WriteAsync(start, NpgsqlDbType.TimestampTz);
                        await copy.WriteAsync(end, NpgsqlDbType.TimestampTz);
                        await copy.WriteAsync(customer, NpgsqlDbType.Uuid);
                        await copy.WriteAsync("Perf customer", NpgsqlDbType.Varchar);
                        await copy.WriteAsync(status, NpgsqlDbType.Varchar);
                        await copy.WriteAsync(created, NpgsqlDbType.TimestampTz);
                        if (status == "Cancelled") await copy.WriteAsync(created.AddHours(1), NpgsqlDbType.TimestampTz);
                        else await copy.WriteNullAsync();
                        await copy.WriteAsync(1, NpgsqlDbType.Integer);
                        rows++;
                    }
                }
            }
            await copy.CompleteAsync();
        }
        await Sql(conn, "ANALYZE");
        Console.WriteLine($"Seeded {rows:N0} bookings for {resources.Count} resources in {watch.Elapsed.TotalSeconds:0.0} s.");
    }

    private static async Task Sql(NpgsqlConnection conn, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
