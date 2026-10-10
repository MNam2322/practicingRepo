namespace BookingApp.Infrastructure.Persistence;

public enum StorageProvider { InMemory, Sqlite, Postgres }

/// <summary>The "Storage" configuration section: where bookings live.</summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>InMemory (lost on restart; tests and quick demos), Sqlite (a file) or Postgres (docker compose up -d).</summary>
    public StorageProvider Provider { get; set; } = StorageProvider.Sqlite;

    /// <summary>
    /// Apply pending EF Core migrations when the app starts. Fine for this single-instance app; with several instances,
    /// run migrations as a deployment step instead (library 7.7 "Migrations with EF Core").
    /// </summary>
    public bool MigrateOnStartup { get; set; } = true;
}

/// <summary>Connection string names under "ConnectionStrings", one per provider.</summary>
public static class ConnectionStrings
{
    public const string Sqlite = "Sqlite";
    public const string Postgres = "Postgres";
}
