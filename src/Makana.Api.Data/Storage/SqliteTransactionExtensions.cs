using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

internal static class SqliteTransactionExtensions
{
    public static SqliteTransaction BeginImmediateTransaction(this SqliteConnection connection)
        => connection.BeginTransaction(deferred: false);
}
