using System.Globalization;
using System.Text.Json;
using Dapper;
using Makana.Journaling.Checkpointing;
using Makana.StackUpdates.Pulumi;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

public sealed class SqliteControlDatabase(string connectionString)
{
    private const int DefaultDeploymentSchemaVersion = DeploymentSchemaVersions.Current;
    private readonly string? _dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;

    public string ConnectionString { get; } = connectionString;

    public SqliteConnection OpenConnection()
    {
        EnsureDatabaseDirectoryExists();

        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        SqliteConnectionInitializer.Configure(connection);
        return connection;
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var tx = connection.BeginImmediateTransaction();

        ReadOnlySpan<string> commands = [
            @"CREATE TABLE IF NOT EXISTS users (
                id TEXT PRIMARY KEY,
                github_login TEXT UNIQUE NOT NULL,
                display_name TEXT NOT NULL,
                email TEXT,
                avatar_url TEXT
            );",
            @"CREATE TABLE IF NOT EXISTS orgs (
                id TEXT PRIMARY KEY,
                login TEXT UNIQUE NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS org_memberships (
                org_id TEXT NOT NULL,
                user_id TEXT NOT NULL,
                PRIMARY KEY (org_id, user_id)
            );",
            @"CREATE TABLE IF NOT EXISTS api_tokens (
                token TEXT PRIMARY KEY,
                user_id TEXT NOT NULL,
                org_id TEXT,
                expires_at TEXT
            );",
            @"CREATE TABLE IF NOT EXISTS user_preferences (
                user_id TEXT PRIMARY KEY,
                theme TEXT,
                updated_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY,
                org_id TEXT NOT NULL,
                name TEXT NOT NULL,
                UNIQUE(org_id, name)
            );",
            @"CREATE TABLE IF NOT EXISTS stacks (
                id TEXT PRIMARY KEY,
                org_id TEXT NOT NULL,
                project_id TEXT NOT NULL,
                name TEXT NOT NULL,
                tags_json TEXT,
                config_json TEXT,
                shard_path TEXT,
                shard_state TEXT,
                shard_migrated_at TEXT,
                UNIQUE(org_id, project_id, name)
            );",
            @"CREATE TABLE IF NOT EXISTS update_locations (
                update_id TEXT PRIMARY KEY,
                stack_id TEXT NOT NULL,
                created_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS deployment_run_locations (
                deployment_run_id TEXT PRIMARY KEY,
                stack_id TEXT NOT NULL,
                created_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS registry_ops (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                payload_json TEXT,
                created_at TEXT NOT NULL,
                completed_at TEXT
            );",
            @"CREATE TABLE IF NOT EXISTS service_secrets (
                name TEXT PRIMARY KEY,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );",
            @"CREATE TABLE IF NOT EXISTS uploads (
                id TEXT PRIMARY KEY,
                path TEXT NOT NULL,
                content_type TEXT,
                content BLOB NOT NULL,
                created_at TEXT NOT NULL
            );",
            @"CREATE INDEX IF NOT EXISTS idx_stacks_shard_state
              ON stacks (shard_state);",
            @"CREATE INDEX IF NOT EXISTS idx_api_tokens_expires_at
              ON api_tokens (expires_at);",
            @"CREATE INDEX IF NOT EXISTS idx_uploads_created_at
              ON uploads (created_at);",
            @"CREATE INDEX IF NOT EXISTS idx_registry_ops_completed_created_at
              ON registry_ops (completed_at, created_at);",
            @"CREATE INDEX IF NOT EXISTS idx_update_locations_stack_id
              ON update_locations (stack_id);",
            @"CREATE INDEX IF NOT EXISTS idx_deployment_run_locations_stack_id
              ON deployment_run_locations (stack_id);",
        ];

        foreach (var command in commands)
        {
            connection.Execute(command, transaction: tx);
        }

        tx.Commit();
    }

    private void EnsureDatabaseDirectoryExists()
    {
        if (string.IsNullOrWhiteSpace(_dataSource) ||
            string.Equals(_dataSource, ":memory:", StringComparison.Ordinal))
        {
            return;
        }

        var fullPath = Path.GetFullPath(_dataSource);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static bool TryDeserializeEngineEvent(string json, out EngineEvent engineEvent)
    {
        try
        {
            engineEvent = JsonSerializer.Deserialize(json, PulumiEngineEventJsonContext.Default.EngineEvent)!;
            return engineEvent is not null && engineEvent.HasSinglePayload;
        }
        catch (JsonException)
        {
            engineEvent = null!;
            return false;
        }
        catch (NotSupportedException)
        {
            engineEvent = null!;
            return false;
        }
    }

    private static bool LooksLikeJournalEntry(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.ValueKind == JsonValueKind.Object &&
               document.RootElement.TryGetProperty("kind", out var kind) &&
               (kind.ValueKind == JsonValueKind.Number || kind.ValueKind == JsonValueKind.String) &&
               document.RootElement.TryGetProperty("sequenceID", out _);
    }
}
