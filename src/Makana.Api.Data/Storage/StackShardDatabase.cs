using Makana.Journaling.Checkpointing;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

public sealed class StackShardDatabase(SqliteControlDatabase controlDatabase)
{
    private readonly string _rootPath = ResolveShardRootPath(controlDatabase.ConnectionString);
    private readonly string _rootPathFull = Path.GetFullPath(ResolveShardRootPath(controlDatabase.ConnectionString));

    public string RootPath => _rootPathFull;

    public string GetShardPath(string namespaceId, string stackId)
        => Path.Join(_rootPathFull, /*namespaceId,*/ $"{stackId}.db");

    public string ResolveCatalogShardPath(string shardPath)
    {
        if (string.IsNullOrWhiteSpace(shardPath))
        {
            throw new InvalidOperationException("A shard path is required.");
        }

        var fullPath = Path.GetFullPath(Path.IsPathRooted(shardPath)
            ? shardPath
            : Path.Join(_rootPathFull, shardPath));

        if (IsUnderRoot(fullPath))
        {
            return fullPath;
        }

        if (TryGetRelativePathUnderStacksRoot(fullPath, out var relativePath))
        {
            return Path.GetFullPath(Path.Join(_rootPathFull, relativePath));
        }

        var fileName = Path.GetFileName(fullPath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            return Path.GetFullPath(Path.Join(_rootPathFull, fileName));
        }

        return fullPath;
    }

    public SqliteConnection OpenConnection(string shardPath)
        => OpenConnection(shardPath, createIfMissing: false);

    private SqliteConnection OpenConnection(string shardPath, bool createIfMissing)
    {
        var resolvedPath = ResolveCatalogShardPath(shardPath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = resolvedPath,
            Mode = createIfMissing ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        SqliteConnectionInitializer.Configure(connection);
        return connection;
    }

    public void EnsureInitialized(string shardPath)
    {
        var resolvedPath = ResolveCatalogShardPath(shardPath);
        Directory.CreateDirectory(Path.GetDirectoryName(resolvedPath) ?? _rootPathFull);

        using var connection = OpenConnection(resolvedPath, createIfMissing: true);
        using var tx = connection.BeginImmediateTransaction();

        using var initCommand = connection.CreateCommand();
        ReadOnlySpan<string> commands = [
                @"CREATE TABLE IF NOT EXISTS updates (
                id TEXT PRIMARY KEY,
                stack_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                kind TEXT NOT NULL,
                status TEXT NOT NULL,
                version INTEGER NOT NULL,
                program_request_json TEXT,
                start_request_json TEXT,
                created_at TEXT NOT NULL,
                completed_at TEXT
            );",
            @"CREATE TABLE IF NOT EXISTS update_tokens (
                token TEXT PRIMARY KEY,
                update_id TEXT NOT NULL,
                expires_at TEXT,
                created_at TEXT NOT NULL
            );",
            $@"CREATE TABLE IF NOT EXISTS deployments (
                id TEXT PRIMARY KEY,
                stack_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                schema_version INTEGER NOT NULL DEFAULT {DeploymentSchemaVersions.Current},
                deployment_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(stack_id, version)
            );",
            @"CREATE TABLE IF NOT EXISTS update_engine_events (
                id TEXT PRIMARY KEY,
                update_id TEXT NOT NULL,
                engine_sequence INTEGER NOT NULL,
                event_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(update_id, engine_sequence)
            );",
            @"CREATE TABLE IF NOT EXISTS update_journal_entries (
                id TEXT PRIMARY KEY,
                update_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                entry_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(update_id, sequence)
            );",
            $@"CREATE TABLE IF NOT EXISTS update_checkpoints (
                id TEXT PRIMARY KEY,
                update_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                version INTEGER NOT NULL,
                schema_version INTEGER NOT NULL DEFAULT {DeploymentSchemaVersions.Current},
                deployment_json TEXT NOT NULL,
                request_signature TEXT,
                created_at TEXT NOT NULL,
                UNIQUE(update_id, sequence)
            );",
            @"CREATE TABLE IF NOT EXISTS deployment_settings (
                stack_id TEXT PRIMARY KEY,
                settings_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS deployment_runs (
                id TEXT PRIMARY KEY,
                stack_id TEXT NOT NULL,
                request_json TEXT NOT NULL,
                created_at TEXT NOT NULL
            );",
        ];

        foreach (var command in commands)
        {
            initCommand.CommandText = command;
            initCommand.ExecuteNonQuery();
        }

        ReadOnlySpan<string> indexes = [
            "CREATE INDEX IF NOT EXISTS idx_updates_stack_status ON updates(stack_id, status);",
            "CREATE INDEX IF NOT EXISTS idx_updates_stack_version ON updates(stack_id, version);",
            "CREATE INDEX IF NOT EXISTS idx_updates_stack_sequence ON updates(stack_id, sequence);",
            "CREATE INDEX IF NOT EXISTS idx_update_tokens_update_id ON update_tokens(update_id);",
            "CREATE INDEX IF NOT EXISTS idx_update_tokens_expires_at ON update_tokens(expires_at);",
            "CREATE INDEX IF NOT EXISTS idx_update_engine_events_update_sequence ON update_engine_events(update_id, engine_sequence);",
            "CREATE INDEX IF NOT EXISTS idx_update_journal_entries_update_sequence ON update_journal_entries(update_id, sequence);",
            "CREATE INDEX IF NOT EXISTS idx_deployment_runs_created_at ON deployment_runs(created_at);"
        ];

        foreach (var index in indexes)
        {
            initCommand.CommandText = index;
            initCommand.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public void DeleteShard(string shardPath)
    {
        var resolvedPath = ResolveCatalogShardPath(shardPath);
        if (File.Exists(resolvedPath))
        {
            using (var connection = OpenConnection(resolvedPath, createIfMissing: false))
            {
                using var checkpoint = connection.CreateCommand();
                checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                checkpoint.ExecuteNonQuery();
                SqliteConnection.ClearPool(connection);
            }

            SqliteConnection.ClearAllPools();
            File.Delete(resolvedPath);
        }

        var shardDirectory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(shardDirectory) &&
            Directory.Exists(shardDirectory) &&
            !Directory.EnumerateFileSystemEntries(shardDirectory).Any())
        {
            Directory.Delete(shardDirectory);
        }
    }

    private bool IsUnderRoot(string fullPath)
    {
        var relativePath = Path.GetRelativePath(_rootPathFull, fullPath);
        return relativePath == "." ||
               (!relativePath.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relativePath));
    }

    private static bool TryGetRelativePathUnderStacksRoot(string fullPath, out string relativePath)
    {
        relativePath = string.Empty;

        var shardPathInfo = new FileInfo(fullPath);
        var directory = shardPathInfo.Directory;
        while (directory is not null && !string.Equals(directory.Name, "stacks", StringComparison.OrdinalIgnoreCase))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            return false;
        }

        relativePath = Path.GetRelativePath(directory.FullName, fullPath);
        return !string.IsNullOrWhiteSpace(relativePath) &&
               !relativePath.StartsWith("..", StringComparison.Ordinal) &&
               !Path.IsPathRooted(relativePath);
    }

    private static string ResolveShardRootPath(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        var dataSource = builder.DataSource;
        if (string.IsNullOrWhiteSpace(dataSource))
        {
            throw new InvalidOperationException("SQLite control database path is required to resolve shard storage.");
        }

        var fullPath = Path.GetFullPath(dataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Unable to resolve the SQLite control database directory.");
        }

        return Path.Join(directory, "stacks");
    }
}
