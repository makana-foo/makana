using Dapper;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

internal static class SqliteConnectionInitializer
{
    private static readonly CommandDefinition PragmaCommand = new("""
        PRAGMA foreign_keys = ON;
        PRAGMA journal_mode = WAL;
        PRAGMA busy_timeout = 5000;
        PRAGMA synchronous = normal;
        PRAGMA temp_store = memory;
        PRAGMA mmap_size = 30000000000;
        """);

    public static void Configure(SqliteConnection connection)
    {
        connection.Execute(PragmaCommand);
    }
}
