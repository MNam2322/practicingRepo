using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingApp.Infrastructure.Persistence;

/// <summary>Recognises the database errors repositories turn into results.</summary>
internal static class DbErrors
{
    /// <summary>A primary key or unique index refused the row.</summary>
    public static bool IsUniqueViolation(DbUpdateException e) => e.InnerException switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode == 19,           // SQLITE_CONSTRAINT (primary key, unique)
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.UniqueViolation,
        _ => false,
    };
}
