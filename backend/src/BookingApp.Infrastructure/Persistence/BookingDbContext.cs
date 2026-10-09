using BookingApp.Core.Bookings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>
/// The booking database. Only the repositories in Infrastructure/Repositories use it; controllers and services never do.
/// Library: 7.7 "DbContext", "Concurrency and transactions"; 7.5 schema design.
/// </summary>
internal sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<BookingRow> Bookings => Set<BookingRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var booking = model.Entity<BookingRow>();
        booking.ToTable("bookings");
        booking.HasKey(b => b.Id);
        booking.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();
        booking.Property(b => b.ResourceId).HasColumnName("resource_id").HasMaxLength(64).IsRequired();
        booking.Property(b => b.StartUtc).HasColumnName("start_utc");
        booking.Property(b => b.EndUtc).HasColumnName("end_utc");
        booking.Property(b => b.CustomerId).HasColumnName("customer_id");
        booking.Property(b => b.CustomerName).HasColumnName("customer_name").HasMaxLength(80).IsRequired();
        booking.Property(b => b.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
        booking.Property(b => b.CreatedAt).HasColumnName("created_at");
        booking.Property(b => b.CancelledAt).HasColumnName("cancelled_at");
        booking.Property(b => b.Version).HasColumnName("version").IsConcurrencyToken();

        // The availability query: blocking bookings for one resource overlapping a window, ordered by start.
        booking.HasIndex(b => new { b.ResourceId, b.StartUtc }).HasDatabaseName("ix_bookings_resource_start");
        // My bookings.
        booking.HasIndex(b => b.CustomerId).HasDatabaseName("ix_bookings_customer");
        booking.ToTable(t => t.HasCheckConstraint("ck_bookings_end_after_start", "end_utc > start_utc"));

        if (Database.IsSqlite())
        {
            // SQLite has no date type and cannot compare or sort DateTimeOffset in SQL. Every time here is UTC, so
            // storing UTC ticks as an INTEGER keeps comparisons, ordering and the index correct.
            var utcTicks = new ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
            var nullableUtcTicks = new ValueConverter<DateTimeOffset?, long?>(
                v => v.HasValue ? v.Value.UtcTicks : null, v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
            booking.Property(b => b.StartUtc).HasConversion(utcTicks);
            booking.Property(b => b.EndUtc).HasConversion(utcTicks);
            booking.Property(b => b.CreatedAt).HasConversion(utcTicks);
            booking.Property(b => b.CancelledAt).HasConversion(nullableUtcTicks);
        }
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the context without starting the API.</summary>
internal sealed class DesignTimeBookingDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BookingDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
