using BookingApp.Core.Bookings;
using BookingApp.Core.Scheduling;
using BookingApp.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>
/// The booking database. Only the repositories in Infrastructure/Repositories use it; controllers and services never do.
/// Rules every writer must obey are constraints here, not just checks in code (library 7.2 "Constraints: the integrity
/// the application cannot enforce"): keys, foreign keys, CHECKs and NOT NULL.
/// </summary>
internal abstract class BookingDbContext(DbContextOptions options) : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<BookingRow> Bookings => Set<BookingRow>();
    public DbSet<VenueRow> Venue => Set<VenueRow>();
    public DbSet<ResourceRow> Resources => Set<ResourceRow>();
    public DbSet<ClosureRow> Closures => Set<ClosureRow>();
    public DbSet<AuditRow> AuditLog => Set<AuditRow>();
    public DbSet<IdempotencyRow> IdempotencyKeys => Set<IdempotencyRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ASP.NET Core Identity's tables (AspNetUsers, AspNetRoles, ...) live in the same database.
        base.OnModelCreating(model);
        model.Entity<AppUser>().Property(u => u.DisplayName).HasMaxLength(AppUser.MaxNameLength).IsRequired();

        var venue = model.Entity<VenueRow>();
        venue.ToTable("venue", t =>
        {
            t.HasCheckConstraint("ck_venue_single_row", "id = 1");
            t.HasCheckConstraint("ck_venue_max_days_ahead", "max_days_ahead BETWEEN 1 AND 365");
            t.HasCheckConstraint("ck_venue_min_lead_minutes", "min_lead_minutes BETWEEN 0 AND 1440");
            t.HasCheckConstraint("ck_venue_cancellation_cutoff", "cancellation_cutoff_hours BETWEEN 0 AND 168");
            t.HasCheckConstraint("ck_venue_max_active", "max_active_bookings BETWEEN 1 AND 100");
        });
        venue.HasKey(v => v.Id);
        venue.Property(v => v.Id).HasColumnName("id").ValueGeneratedNever();
        venue.Property(v => v.Name).HasColumnName("name").HasMaxLength(100).IsRequired();
        venue.Property(v => v.TimeZoneId).HasColumnName("time_zone_id").HasMaxLength(64).IsRequired();
        // The booking rules live with the venue so an admin can change them (the migration fills existing rows).
        venue.Property(v => v.MaxDaysAhead).HasColumnName("max_days_ahead");
        venue.Property(v => v.MinLeadMinutes).HasColumnName("min_lead_minutes");
        venue.Property(v => v.CancellationCutoffHours).HasColumnName("cancellation_cutoff_hours");
        venue.Property(v => v.MaxActiveBookingsPerCustomer).HasColumnName("max_active_bookings");

        // Who changed what (library 10.6 "Auditing and compliance"): the actor is the signed-in account, not the
        // connection. Append-only: the migration adds a trigger that refuses UPDATE and DELETE.
        var audit = model.Entity<AuditRow>();
        audit.ToTable("audit_log");
        audit.HasKey(a => a.Id);
        audit.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        audit.Property(a => a.OccurredAt).HasColumnName("occurred_at");
        audit.Property(a => a.ActorId).HasColumnName("actor_id");
        audit.Property(a => a.ActorEmail).HasColumnName("actor_email").HasMaxLength(254).IsRequired();
        audit.Property(a => a.RequestId).HasColumnName("request_id").HasMaxLength(64);
        audit.Property(a => a.Action).HasColumnName("action").HasMaxLength(64).IsRequired();
        audit.Property(a => a.Entity).HasColumnName("entity").HasMaxLength(32).IsRequired();
        audit.Property(a => a.EntityId).HasColumnName("entity_id").HasMaxLength(64).IsRequired();
        audit.Property(a => a.Before).HasColumnName("before");
        audit.Property(a => a.After).HasColumnName("after");
        audit.HasIndex(a => a.OccurredAt).HasDatabaseName("ix_audit_log_occurred_at");
        audit.HasIndex(a => new { a.Entity, a.EntityId, a.OccurredAt }).HasDatabaseName("ix_audit_log_entity");

        // Idempotency keys: the primary key (scope, key) makes claiming a key atomic (library 8.2).
        var idempotency = model.Entity<IdempotencyRow>();
        idempotency.ToTable("idempotency_keys");
        idempotency.HasKey(i => new { i.Scope, i.Key });
        idempotency.Property(i => i.Scope).HasColumnName("scope").HasMaxLength(64);
        idempotency.Property(i => i.Key).HasColumnName("key").HasMaxLength(100);
        idempotency.Property(i => i.RequestHash).HasColumnName("request_hash").HasMaxLength(64).IsRequired();
        idempotency.Property(i => i.ClaimedAt).HasColumnName("claimed_at");
        idempotency.Property(i => i.ResponseStatus).HasColumnName("response_status");
        idempotency.Property(i => i.ResponseBody).HasColumnName("response_body");
        idempotency.Property(i => i.ResponseContentType).HasColumnName("response_content_type").HasMaxLength(100);
        idempotency.Property(i => i.ResponseLocation).HasColumnName("response_location").HasMaxLength(200);
        idempotency.HasIndex(i => i.ClaimedAt).HasDatabaseName("ix_idempotency_keys_claimed_at");

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
        booking.Property(b => b.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(Booking.MaxReasonLength);
        booking.Property(b => b.HoldExpiresAt).HasColumnName("hold_expires_at");
        // The expiry job's query: holds still Pending, by expiry time.
        booking.HasIndex(b => new { b.Status, b.HoldExpiresAt }).HasDatabaseName("ix_bookings_status_hold_expires");
        booking.Property(b => b.Version).HasColumnName("version").IsConcurrencyToken();
        // A resource with bookings cannot be deleted by accident: RESTRICT, the right default (library 7.2).
        booking.HasOne<ResourceRow>().WithMany().HasForeignKey(b => b.ResourceId).OnDelete(DeleteBehavior.Restrict);

        // The availability query: blocking bookings for one resource overlapping a window, ordered by start.
        booking.HasIndex(b => new { b.ResourceId, b.StartUtc }).HasDatabaseName("ix_bookings_resource_start");
        // My bookings.
        booking.HasIndex(b => b.CustomerId).HasDatabaseName("ix_bookings_customer");

        ConfigureProvider(model);
    }

    /// <summary>What differs per database: column types, extensions and the constraints only one engine has.</summary>
    protected abstract void ConfigureProvider(ModelBuilder model);
}

/// <summary>
/// The InMemory storage provider keeps bookings and resources in the lock-based in-memory repositories; this context
/// (EF Core's in-memory database) only holds accounts, so sign-in works there too. No migrations: created on start.
/// </summary>
internal sealed class InMemoryBookingDbContext(DbContextOptions<InMemoryBookingDbContext> options) : BookingDbContext(options)
{
    protected override void ConfigureProvider(ModelBuilder model)
    {
        // The in-memory database cannot store the computed range or enforce CHECKs; nothing provider-specific is needed.
    }
}

/// <summary>SQLite: a file, no date type. Migrations in Migrations/Sqlite.</summary>
internal sealed class SqliteBookingDbContext(DbContextOptions<SqliteBookingDbContext> options) : BookingDbContext(options)
{
    protected override void ConfigureProvider(ModelBuilder model)
    {
        // SQLite has no date type and cannot compare or sort DateTimeOffset in SQL. Every time here is UTC, so
        // storing UTC ticks as an INTEGER keeps comparisons, ordering and the index correct.
        var booking = model.Entity<BookingRow>();
        var closure = model.Entity<ClosureRow>();
        UtcTicks(booking.Property(b => b.StartUtc), booking.Property(b => b.EndUtc), booking.Property(b => b.CreatedAt),
            closure.Property(c => c.StartUtc), closure.Property(c => c.EndUtc), closure.Property(c => c.CreatedAt),
            model.Entity<AuditRow>().Property(a => a.OccurredAt), model.Entity<IdempotencyRow>().Property(i => i.ClaimedAt));
        var nullableTicks = new ValueConverter<DateTimeOffset?, long?>(
            v => v.HasValue ? v.Value.UtcTicks : null, v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
        booking.Property(b => b.CancelledAt).HasConversion(nullableTicks);
        booking.Property(b => b.HoldExpiresAt).HasConversion(nullableTicks);
    }

    private static void UtcTicks(params PropertyBuilder<DateTimeOffset>[] properties)
    {
        var converter = new ValueConverter<DateTimeOffset, long>(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
        foreach (var property in properties) property.HasConversion(converter);
    }
}

/// <summary>
/// PostgreSQL: times are timestamptz, and the database itself refuses overlapping confirmed bookings with an exclusion
/// constraint over a tstzrange (added in the migration; library 7.2 "EXCLUDE", 7.3 "Specialised index types").
/// Migrations in Migrations/Postgres.
/// </summary>
internal sealed class PostgresBookingDbContext(DbContextOptions<PostgresBookingDbContext> options) : BookingDbContext(options)
{
    /// <summary>Name of the exclusion constraint; violating it raises SQLSTATE 23P01.</summary>
    public const string NoOverlapConstraint = "ex_bookings_no_overlap";

    protected override void ConfigureProvider(ModelBuilder model)
    {
        // btree_gist lets a GiST index (needed for range overlap, &&) also compare resource_id with =.
        model.HasPostgresExtension("btree_gist");
        // [start, end) as a range, kept in step by the database. The exclusion constraint is built on it.
        model.Entity<BookingRow>().Property<NpgsqlTypes.NpgsqlRange<DateTime>>("Period")
            .HasColumnName("period")
            .HasComputedColumnSql("tstzrange(start_utc, end_utc, '[)')", stored: true);
    }
}

/// <summary>Lets <c>dotnet ef migrations add --context SqliteBookingDbContext</c> build the context without starting the API.</summary>
internal sealed class DesignTimeSqliteFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<SqliteBookingDbContext>
{
    public SqliteBookingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<SqliteBookingDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

/// <summary>For <c>dotnet ef migrations add --context PostgresBookingDbContext</c>. No database is contacted.</summary>
internal sealed class DesignTimePostgresFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<PostgresBookingDbContext>
{
    public PostgresBookingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PostgresBookingDbContext>().UseNpgsql("Host=localhost;Database=design_time").Options);
}
