using BookingApp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BookingApp.Infrastructure.Tests;

/// <summary>Migrations must upgrade a database that already has data, not only create an empty one.</summary>
public sealed class MigrationTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"booking-migrate-{Guid.NewGuid():N}.db");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bookings_made_before_resources_moved_to_the_database_survive_the_upgrade()
    {
        await using (var db = TestDatabase.Context(_file))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261009165414_InitialCreate", Ct);
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO bookings (id, resource_id, start_utc, end_utc, customer_id, customer_name, status, created_at, version) " +
                "VALUES ('6D1F1C3E-0000-4000-8000-000000000001', 'court-9', 1, 2, '6D1F1C3E-0000-4000-8000-000000000002', 'Old booking', 'Confirmed', 0, 1)", Ct);
        }

        await using (var db = TestDatabase.Context(_file))
        {
            await db.Database.MigrateAsync(Ct);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync(Ct));
            var placeholder = await db.Resources.SingleAsync(r => r.Id == "court-9", Ct);
            Assert.Equal(Core.Scheduling.ResourceKind.Other, placeholder.Kind);
            Assert.Equal("Old booking", (await db.Bookings.SingleAsync(Ct)).CustomerName);
        }
    }

    public void Dispose() => TestDatabase.Delete(_file);
}
