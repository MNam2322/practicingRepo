using BookingApp.Core.Admin;
using BookingApp.Core.Bookings;
using BookingApp.Core.Common;
using BookingApp.Core.Notifications;
using BookingApp.Core.Payments;
using BookingApp.Core.Scheduling;
using BookingApp.Core.Waitlist;
using BookingApp.Infrastructure.Identity;
using BookingApp.Infrastructure.Notifications;
using BookingApp.Infrastructure.Payments;
using BookingApp.Infrastructure.Persistence;
using BookingApp.Infrastructure.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookingApp.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers the adapters behind the ports in BookingApp.Core.</summary>
    /// <param name="contentRoot">Relative SQLite file paths are resolved against this folder.</param>
    public static IServiceCollection AddBookingInfrastructure(this IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddOptions<VenueOptions>()
            .Bind(configuration.GetSection(VenueOptions.Section))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Name), "Venue:Name is required.")
            .Validate(o => o.Resources.Count > 0, "Venue:Resources needs at least one resource.")
            .Validate(o => o.Resources.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == o.Resources.Count, "Resource ids must be unique.")
            .ValidateOnStart();

        // The "Booking" section only seeds the rules: after the first run they live with the venue (admins change them).
        services.AddOptions<BookingPolicy>().Bind(configuration.GetSection("Booking"));

        var storage = configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        switch (storage.Provider)
        {
            case StorageProvider.InMemory:
                services.AddSingleton<ConfigurationResourceRepository>();
                services.AddSingleton<IResourceRepository>(sp => sp.GetRequiredService<ConfigurationResourceRepository>());
                services.AddSingleton<IVenueAdminRepository>(sp => sp.GetRequiredService<ConfigurationResourceRepository>());
                services.AddSingleton<InMemoryOutbox>();
                services.AddSingleton<INotificationOutbox>(sp => sp.GetRequiredService<InMemoryOutbox>());
                services.AddSingleton<IBookingRepository>(sp => new InMemoryBookingRepository(sp.GetRequiredService<InMemoryOutbox>()));
                services.AddSingleton<IWaitlistRepository, InMemoryWaitlistRepository>();
                services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();
                services.AddSingleton<IPaymentRepository, InMemoryPaymentRepository>();
                // Accounts only; one in-memory database per app instance.
                var name = $"accounts-{Guid.NewGuid():N}";
                services.AddDbContext<InMemoryBookingDbContext>(o => o.UseInMemoryDatabase(name));
                services.AddScoped<BookingDbContext>(sp => sp.GetRequiredService<InMemoryBookingDbContext>());
                services.AddHostedService<DatabaseInitializer>();
                break;
            case StorageProvider.Sqlite:
                var sqlite = SqliteConnectionString(configuration.GetConnectionString(ConnectionStrings.Sqlite), contentRoot);
                services.AddDbContext<SqliteBookingDbContext>(o => o.UseSqlite(sqlite));
                AddEfRepositories<SqliteBookingDbContext>(services);
                break;
            case StorageProvider.Postgres:
                var postgres = configuration.GetConnectionString(ConnectionStrings.Postgres)
                    ?? throw new InvalidOperationException("Storage:Provider is Postgres but ConnectionStrings:Postgres is not set.");
                // Overlapping inserts racing on the exclusion constraint can deadlock (SQLSTATE 40P01); PostgreSQL rolls
                // one back. The retrying execution strategy runs it again, and it then succeeds or gets 23P01 (slot_taken).
                // Library 7.7 "Concurrency and transactions": an execution strategy retries whole operations.
                services.AddDbContext<PostgresBookingDbContext>(o => o.UseNpgsql(postgres, npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5)));
                AddEfRepositories<PostgresBookingDbContext>(services);
                break;
            default:
                throw new InvalidOperationException($"Unknown Storage:Provider '{storage.Provider}'.");
        }

        AddAccounts(services);
        services.AddOptions<HoldOptions>().Bind(configuration.GetSection(HoldOptions.Section))
            .Validate(o => o.Minutes > 0 && o.SweepSeconds > 0, "Holds:Minutes and Holds:SweepSeconds must be positive.").ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<HoldOptions>>().Value);
        services.AddHostedService<HoldExpiryService>();
        AddPayments(services, configuration);
        AddNotifications(services, configuration, contentRoot);
        services.AddOptions<AccountBootstrapOptions>().Bind(configuration.GetSection(AccountBootstrapOptions.Section));
        if (configuration.GetValue("Seed:DemoBookings", false)) services.AddHostedService<DemoSeeder>();
        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity over the booking database. Passwords: at least 10 characters and no forced character
    /// classes (length beats complexity rules); five wrong passwords lock the account for five minutes.
    /// The auth cookie itself is configured by the Api (it is an HTTP concern).
    /// </summary>
    private static void AddAccounts(IServiceCollection services)
    {
        services.AddIdentityCore<AppUser>(o =>
            {
                o.User.RequireUniqueEmail = true;
                o.Password.RequiredLength = 10;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequiredUniqueChars = 4;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<BookingDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        services.AddScoped<AccountService>();
    }

    /// <summary>
    /// Deposits through the fake payment provider (the only one so far; a real one is another adapter behind the same
    /// ports). The webhook secret is required: without it no webhook could be verified, so the app refuses to start.
    /// </summary>
    private static void AddPayments(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaymentOptions>().Bind(configuration.GetSection(PaymentOptions.Section))
            .Validate(o => Money.IsSupported(o.Currency), "Payments:Currency must be a supported ISO 4217 code (such as VND or USD).")
            .Validate(o => o.Deposit >= 0 && (!Money.IsSupported(o.Currency) || o.Deposit == decimal.Round(o.Deposit, Money.ExponentOf(o.Currency))),
                "Payments:Deposit must be zero or more, with no more decimals than the currency has.")
            .Validate(o => o.SweepSeconds > 0, "Payments:SweepSeconds must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<PaymentOptions>>().Value);
        services.AddOptions<FakePaymentOptions>().Bind(configuration.GetSection(FakePaymentOptions.Section))
            .Validate(o => o.WebhookSecret.Length >= 16, "Payments:Fake:WebhookSecret must be set (16+ characters), for example with the environment variable Payments__Fake__WebhookSecret.")
            .Validate(o => o.WebhookToleranceSeconds > 0, "Payments:Fake:WebhookToleranceSeconds must be positive.")
            .ValidateOnStart();
        services.AddSingleton<FakePaymentGateway>();
        services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakePaymentGateway>());
        services.AddSingleton<IPaymentWebhookReader, FakePaymentWebhookReader>();
        services.AddHostedService<RefundService>();
    }

    /// <summary>
    /// Emails through the notification outbox: the relay sends what is due through the file sender (.eml files in
    /// Email:Folder, relative to the API's folder).
    /// </summary>
    private static void AddNotifications(IServiceCollection services, IConfiguration configuration, string contentRoot)
    {
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.Section))
            .Validate(o => o.PollSeconds > 0 && o.MaxAttempts > 0, "Notifications:PollSeconds and Notifications:MaxAttempts must be positive.")
            .Validate(o => Uri.TryCreate(o.AppUrl, UriKind.Absolute, out _), "Notifications:AppUrl must be an absolute URL.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<NotificationOptions>>().Value);
        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section))
            .PostConfigure(o => o.Folder = Path.GetFullPath(Path.Combine(contentRoot, o.Folder)))
            .Validate(o => o.DelaySeconds >= 0, "Email:DelaySeconds must not be negative.")
            .ValidateOnStart();
        services.AddSingleton<FileEmailSender>();
        services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<FileEmailSender>());
        services.AddHostedService<NotificationRelay>();

        services.AddOptions<WaitlistOptions>().Bind(configuration.GetSection(WaitlistOptions.Section))
            .Validate(o => o.SweepSeconds > 0 && o.OfferMinutes > 0, "Waitlist:SweepSeconds and Waitlist:OfferMinutes must be positive.")
            .ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<WaitlistOptions>>().Value);
        services.AddHostedService<WaitlistSweep>();
    }

    /// <summary>The EF Core repositories over whichever database was chosen; they depend only on the shared BookingDbContext.</summary>
    private static void AddEfRepositories<TContext>(IServiceCollection services) where TContext : BookingDbContext
    {
        services.AddScoped<BookingDbContext>(sp => sp.GetRequiredService<TContext>());
        services.AddScoped<IBookingRepository, EfBookingRepository>();
        services.AddScoped<IResourceRepository, EfResourceRepository>();
        services.AddScoped<IVenueAdminRepository, EfVenueAdminRepository>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IPaymentRepository, EfPaymentRepository>();
        services.AddScoped<INotificationOutbox, EfNotificationOutbox>();
        services.AddScoped<IWaitlistRepository, EfWaitlistRepository>();
        // First hosted service: the schema exists before anything else (such as the demo seeder) uses it.
        services.AddHostedService<DatabaseInitializer>();
        // After the initializer, so its first run finds the table.
        services.AddHostedService<IdempotencyKeyCleaner>();
    }

    /// <summary>"Data Source=.data/bookings.db" is relative to the API's folder; the folder is created if needed.</summary>
    private static string SqliteConnectionString(string? configured, string contentRoot)
    {
        var builder = new SqliteConnectionStringBuilder(configured ?? "Data Source=.data/bookings.db");
        if (builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
            builder.DataSource = Path.GetFullPath(Path.Combine(contentRoot, builder.DataSource));
        if (Path.GetDirectoryName(builder.DataSource) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
        return builder.ToString();
    }
}

/// <summary>
/// Adds demo bookings (tomorrow, and earlier today) so the grids and the staff schedule are not empty. Development only.
/// Repositories may be scoped (EF Core), so it works inside its own scope.
/// </summary>
internal sealed partial class DemoSeeder(IServiceScopeFactory scopes, TimeProvider time, ILogger<DemoSeeder> logger) : IHostedService
{
    private static readonly CustomerId DemoCustomer = new(Guid.Parse("00000000-0000-7000-8000-000000000001"));

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var bookings = scope.ServiceProvider.GetRequiredService<IBookingRepository>();
        var resources = scope.ServiceProvider.GetRequiredService<IResourceRepository>();
        var catalog = await resources.GetCatalogAsync(cancellationToken);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), catalog.Venue.TimeZone).DateTime);
        var count = 0;
        foreach (var resource in catalog.Resources)
        {
            // Tomorrow: two booked slots, so the grid is not empty. Today: the first two slots, but only if they have
            // already started, so staff can try "completed" and "no-show" straight away.
            count += await SeedAsync(bookings, resources, catalog.Venue, resource, today.AddDays(1), [2, 5], onlyStarted: false, cancellationToken);
            count += await SeedAsync(bookings, resources, catalog.Venue, resource, today, [0, 1], onlyStarted: true, cancellationToken);
        }
        LogSeeded(count, today);
    }

    private async Task<int> SeedAsync(IBookingRepository bookings, IResourceRepository resources, Venue venue, Resource resource, DateOnly day,
        int[] slotOffsets, bool onlyStarted, CancellationToken cancellationToken)
    {
        var hours = resource.HoursOn(day.DayOfWeek);
        if (hours is null) return 0;
        var count = 0;
        foreach (var offset in slotOffsets)
        {
            var localStart = day.ToDateTime(hours.Open).AddMinutes(resource.SlotMinutes * offset);
            if (TimeOnly.FromDateTime(localStart.AddMinutes(resource.SlotMinutes)) > hours.Close) continue;
            var start = AvailabilityCalculator.ToUtc(localStart, venue);
            if (onlyStarted && start > time.GetUtcNow()) continue;
            var range = TimeRange.FromStart(start, resource.SlotLength);
            // Demo bookings obey the same closures as real ones.
            if ((await resources.ClosuresAsync(resource.Id, range, cancellationToken)).Count > 0) continue;
            var booking = Booking.Create(resource.Id, range, DemoCustomer, "Demo customer", time.GetUtcNow());
            if (await bookings.TryAddAsync(booking, cancellationToken)) count++;
        }
        return count;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Count} demo bookings for today ({Day}) and tomorrow")]
    private partial void LogSeeded(int count, DateOnly day);
}
