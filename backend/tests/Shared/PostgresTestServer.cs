using Npgsql;
using Testcontainers.PostgreSql;

namespace BookingApp.Tests.Shared;

/// <summary>
/// One real PostgreSQL 17 per test run (Testcontainers), and a fresh database for each test that asks, so tests never
/// share rows (library 4.6 "Integration testing with real dependencies", "Tests must not share state").
/// Without Docker the PostgreSQL tests are skipped with a message, unless REQUIRE_DOCKER=1 (set it in CI), which
/// turns a missing Docker into a failure so the database tests cannot silently stop running.
/// </summary>
internal static class PostgresTestServer
{
    private static readonly Lazy<Task<(PostgreSqlContainer? Container, string? Problem)>> Server = new(StartAsync);

    /// <summary>A connection string to a new, empty database on the shared server.</summary>
    public static async Task<string> NewDatabaseAsync()
    {
        var (container, problem) = await Server.Value;
        if (container is null)
        {
            var message = $"PostgreSQL tests need Docker: {problem}";
            if (Environment.GetEnvironmentVariable("REQUIRE_DOCKER") == "1") throw new InvalidOperationException(message);
            Assert.Skip(message);
        }

        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(container.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await create.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ToString();
    }

    /// <summary>Closes the idle pooled connections to a test database once its test is done.</summary>
    public static void Release(string? connectionString)
    {
        if (connectionString is null) return;
        using var connection = new NpgsqlConnection(connectionString);
        NpgsqlConnection.ClearPool(connection);
    }

    private static async Task<(PostgreSqlContainer?, string?)> StartAsync()
    {
        try
        {
            // Every test has its own database and so its own connection pool; allow plenty of connections.
            var container = new PostgreSqlBuilder("postgres:17-alpine").WithCommand("-c", "max_connections=500").Build();
            await container.StartAsync();
            // Testcontainers' reaper removes the container when the test process exits.
            return (container, null);
        }
        catch (Exception e)
        {
            return (null, e.Message);
        }
    }
}
