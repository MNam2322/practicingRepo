using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>
/// The booking database. Only the repositories in Infrastructure/Repositories use it; controllers and services never do.
/// Rules every writer must obey are constraints here, not just checks in code (library 7.2 "Constraints: the integrity
/// the application cannot enforce"): keys, foreign keys, CHECKs and NOT NULL.
/// </summary>
internal sealed class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<BookingRow> Bookings => Set<BookingRow>();
    public DbSet<VenueRow> Venue => Set<VenueRow>();
    public DbSet<ResourceRow> Resources => Set<ResourceRow>();
    public DbSet<ClosureRow> Closures => Set<ClosureRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var venue = model.Entity<VenueRow>();
        venue.ToTable("venue", t => t.HasCheckConstraint("ck_venue_single_row", "id = 1"));
        venue.HasKey(v => v.Id);
        venue.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();
        venue.Property(v => v.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        venue.Property(v => v.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(64).IsRequired();

        var resource = model.Entity<ResourceRow>();
        resource.ToTable("resources", t =>
        {
            t.HasCheckConstraint("ck_resources_capacity", "capacity > 0");
            t.HasCheckConstraint("ck_resources_slot_minutes", "slot_minutes > 0 AND slot_minutes <= 1440");
            t.HasCheckConstraint("ck_resources_max_slots", "max_slots_per_booking >= 1");
            t.HasCheckConstraint("ck_resources_kind", "kind IN ('Court', 'Room', 'Table', 'Other')");
        });
        resource.HasKey(r => r.Id);
        resource.Property(r => r.Id).HasColumnName("id").HasMaxLength(64).ValueGeneratedNever();
        resource.Property(r => r.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        resource.Property(r => r.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        resource.Property(r => r.Capacity).HasColumnName("capacity");
        resource.Property(r => r.SlotMinutes).HasColumnName("slot_minutes");
        resource.Property(r => r.MaxSlotsPerBooking).HasColumnName("max_slots_per_booking");
        resource.Property(r => r.SortOrder).HasColumnName("sort_order");
        resource.HasMany(r => r.Hours).WithOne().HasForeignKey(h => h.ResourceId).OnDelete(DeleteBehavior.Cascade);

        var hours = model.Entity<OpeningHoursRow>();
        hours.ToTable("opening_hours", t =>
        {
            t.HasCheckConstraint("ck_opening_hours_day", "day_of_week BETWEEN 0 AND 6");
            t.HasCheckConstraint("ck_opening_hours_close_after_open", "close_time > open_time");
        });
        hours.HasKey(h => new { h.ResourceId, h.Day });
        hours.Property(h => h.ResourceId).HasColumnName("resource_id").HasMaxLength(64);
        hours.Property(h => h.Day).HasColumnName("day_of_week");
        hours.Property(h => h.Open).HasColumnName("open_time");
        hours.Property(h => h.Close).HasColumnName("close_time");

        var closure = model.Entity<ClosureRow>();
        closure.ToTable("closures", t => t.HasCheckConstraint("ck_closures_end_after_start", "end_utc > start_utc"));
        closure.HasKey(c => c.Id);
        closure.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
        closure.Property(c => c.ResourceId).HasColumnName("resource_id").HasMaxLength(64);
        closure.Property(c => c.StartUtc).HasColumnName("start_utc");
        closure.Property(c => c.EndUtc).HasColumnName("end_utc");
        closure.Property(c => c.Reason).HasColumnName("reason").HasMaxLength(200).IsRequired();
        closure.Property(c => c.CreatedAt).HasColumnName("created_at");
        closure.HasOne<ResourceRow>().WithMany().HasForeignKey(c => c.ResourceId).OnDelete(DeleteBehavior.Cascade);
        closure.HasIndex(c => new { c.ResourceId, c.StartUtc }).HasDatabaseName("ix_closures_resource_start");

        var booking = model.Entity<BookingRow>();
        booking.ToTable("bookings", t => t.HasCheckConstraint("ck_bookings_end_after_start", "end_utc > start_utc"));
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
        // A resource with bookings cannot be deleted by accident: RESTRICT, the right default (library 7.2).
        booking.HasOne<ResourceRow>().WithMany().HasForeignKey(b => b.ResourceId).OnDelete(DeleteBehavior.Restrict);

        // The availability query: blocking bookings for one resource overlapping a window, ordered by start.
        booking.HasIndex(b => new { b.ResourceId, b.StartUtc }).HasDatabaseName("ix_bookings_resource_start");
        // My bookings.
        booking.HasIndex(b => b.CustomerId).HasDatabaseName("ix_bookings_customer");

        if (Database.IsSqlite())
        {
            // SQLite has no date type and cannot compare or sort DateTimeOffset in SQL. Every time here is UTC, so
            // storing UTC ticks as an INTEGER keeps comparisons, ordering and the index correct.
            UtcTicks(booking.Property(b => b.StartUtc), booking.Property(b => b.EndUtc), booking.Property(b => b.CreatedAt),
                closure.Property(c => c.StartUtc), closure.Property(c => c.EndUtc), closure.Property(c => c.CreatedAt));
            booking.Property(b => b.CancelledAt).HasConversion(new ValueConverter<DateTimeOffset?, long?>(
                v => v.HasValue ? v.Value.UtcTicks : null, v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null));
        }
    }

    private static void UtcTicks(params PropertyBuilder<DateTimeOffset>[] properties)
    {
        var converter = new ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        foreach (var property in properties) property.HasConversion(converter);
    }
}

/// <summary>Lets <c>dotnet ef migrations add</c> build the context without starting the API.</summary>
internal sealed class DesignTimeBookingDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<BookingDbContext>
{
    public BookingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<BookingDbContext>().UseSqlite("Data Source=design-time.db").Options);
}
