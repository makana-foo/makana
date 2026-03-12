using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

internal static class SqliteConnectionInitializer
{
    public static void Configure(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA foreign_keys = ON;
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA synchronous = normal;
            PRAGMA temp_store = memory;
            PRAGMA mmap_size = 30000000000;
            """;
        cmd.ExecuteNonQuery();
    }
}
