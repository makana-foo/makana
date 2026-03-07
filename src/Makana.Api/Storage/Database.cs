using System.Text.Json;
using Makana.StackUpdates.Pulumi;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

public sealed class Database(string connectionString)
{
    public string ConnectionString { get; } = connectionString;

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
        return connection;
    }

    public async Task InitializeAsync()
    {
        using var connection = OpenConnection();
        var commands = new[]
        {
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
                UNIQUE(org_id, project_id, name)
            );",
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
                expires_at TEXT
            );",
            @"CREATE TABLE IF NOT EXISTS deployments (
                id TEXT PRIMARY KEY,
                stack_id TEXT NOT NULL,
                version INTEGER NOT NULL,
                deployment_json TEXT NOT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(stack_id, version)
            );",
            @"CREATE TABLE IF NOT EXISTS update_events (
                id TEXT PRIMARY KEY,
                update_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                event_json TEXT NOT NULL,
                created_at TEXT NOT NULL
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
            @"CREATE TABLE IF NOT EXISTS update_checkpoints (
                id TEXT PRIMARY KEY,
                update_id TEXT NOT NULL,
                sequence INTEGER NOT NULL,
                version INTEGER NOT NULL,
                deployment_json TEXT NOT NULL,
                created_at TEXT NOT NULL
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
        };

        foreach (var command in commands)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = command;
            await cmd.ExecuteNonQueryAsync();
        }

        await MigrateUpdatesSchemaAsync(connection);
        await MigrateEventStorageSchemaAsync(connection);
        await MigrateUpdateCheckpointSchemaAsync(connection);
        // Cleanup migration for previous personal-namespace modeling that incorrectly
        // created synthetic org rows. Personal namespaces should map to users, not orgs.
        await MigrateLegacyPersonalNamespaceRowsAsync(connection);
        await MigrateOwnerLoginCollisionsAsync(connection);
        await MigrateOwnerLoginConstraintsAsync(connection);
    }

    private static async Task MigrateUpdatesSchemaAsync(SqliteConnection connection)
    {
        var hasSequence = false;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(updates);";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), "sequence", StringComparison.Ordinal))
                {
                    hasSequence = true;
                    break;
                }
            }
        }

        if (!hasSequence)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE updates ADD COLUMN sequence INTEGER;";
            await alter.ExecuteNonQueryAsync();
        }

        using (var backfill = connection.CreateCommand())
        {
            backfill.CommandText = @"
                UPDATE updates
                SET sequence = (
                    SELECT COUNT(*)
                    FROM updates u2
                    WHERE u2.stack_id = updates.stack_id
                      AND (
                          u2.created_at < updates.created_at OR
                          (u2.created_at = updates.created_at AND u2.id <= updates.id)
                      )
                )
                WHERE sequence IS NULL;";
            await backfill.ExecuteNonQueryAsync();
        }

        var indexes = new[]
        {
            "CREATE INDEX IF NOT EXISTS idx_updates_stack_status ON updates(stack_id, status);",
            "CREATE INDEX IF NOT EXISTS idx_updates_stack_version ON updates(stack_id, version);",
            "CREATE INDEX IF NOT EXISTS idx_updates_stack_sequence ON updates(stack_id, sequence);"
        };

        foreach (var index in indexes)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = index;
            await cmd.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrateUpdateCheckpointSchemaAsync(SqliteConnection connection)
    {
        var hasRequestSignature = false;
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(update_checkpoints);";
            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (string.Equals(reader.GetString(1), "request_signature", StringComparison.Ordinal))
                {
                    hasRequestSignature = true;
                    break;
                }
            }
        }

        if (!hasRequestSignature)
        {
            using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE update_checkpoints ADD COLUMN request_signature TEXT;";
            await alter.ExecuteNonQueryAsync();
        }

        using (var index = connection.CreateCommand())
        {
            index.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS idx_update_checkpoints_update_sequence
                ON update_checkpoints(update_id, sequence);";
            await index.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrateEventStorageSchemaAsync(SqliteConnection connection)
    {
        var indexes = new[]
        {
            "CREATE INDEX IF NOT EXISTS idx_update_engine_events_update_sequence ON update_engine_events(update_id, engine_sequence);",
            "CREATE INDEX IF NOT EXISTS idx_update_journal_entries_update_sequence ON update_journal_entries(update_id, sequence);"
        };

        foreach (var index in indexes)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = index;
            await cmd.ExecuteNonQueryAsync();
        }

        var hasLegacyRows = false;
        using (var countCmd = connection.CreateCommand())
        {
            countCmd.CommandText = "SELECT EXISTS(SELECT 1 FROM update_events LIMIT 1);";
            hasLegacyRows = Convert.ToInt32(await countCmd.ExecuteScalarAsync()) == 1;
        }

        if (!hasLegacyRows)
        {
            return;
        }

        using var select = connection.CreateCommand();
        select.CommandText = "SELECT id, update_id, sequence, event_json, created_at FROM update_events ORDER BY update_id, sequence ASC;";
        using var reader = await select.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetString(0);
            var updateId = reader.GetString(1);
            var sequence = reader.GetInt32(2);
            var json = reader.GetString(3);
            var createdAt = reader.GetString(4);

            if (TryDeserializeEngineEvent(json, out var engineEvent))
            {
                using var insertEngine = connection.CreateCommand();
                insertEngine.CommandText = @"
                    INSERT OR IGNORE INTO update_engine_events (id, update_id, engine_sequence, event_json, created_at)
                    VALUES (@id, @updateId, @engineSequence, @event, @createdAt);";
                insertEngine.Parameters.AddWithValue("@id", id);
                insertEngine.Parameters.AddWithValue("@updateId", updateId);
                insertEngine.Parameters.AddWithValue("@engineSequence", engineEvent.Sequence);
                insertEngine.Parameters.AddWithValue("@event", json);
                insertEngine.Parameters.AddWithValue("@createdAt", createdAt);
                await insertEngine.ExecuteNonQueryAsync();
                continue;
            }

            if (LooksLikeJournalEntry(json))
            {
                using var insertJournal = connection.CreateCommand();
                insertJournal.CommandText = @"
                    INSERT OR IGNORE INTO update_journal_entries (id, update_id, sequence, entry_json, created_at)
                    VALUES (@id, @updateId, @sequence, @entry, @createdAt);";
                insertJournal.Parameters.AddWithValue("@id", id);
                insertJournal.Parameters.AddWithValue("@updateId", updateId);
                insertJournal.Parameters.AddWithValue("@sequence", sequence);
                insertJournal.Parameters.AddWithValue("@entry", json);
                insertJournal.Parameters.AddWithValue("@createdAt", createdAt);
                await insertJournal.ExecuteNonQueryAsync();
            }
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

    private static async Task MigrateLegacyPersonalNamespaceRowsAsync(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE projects
            SET org_id = (
                SELECT u.id
                FROM users u
                JOIN orgs o ON o.login = u.github_login
                WHERE o.id = projects.org_id
                  AND o.id LIKE 'personal-%'
                LIMIT 1
            )
            WHERE EXISTS (
                SELECT 1
                FROM users u
                JOIN orgs o ON o.login = u.github_login
                WHERE o.id = projects.org_id
                  AND o.id LIKE 'personal-%'
            );";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            UPDATE stacks
            SET org_id = (
                SELECT u.id
                FROM users u
                JOIN orgs o ON o.login = u.github_login
                WHERE o.id = stacks.org_id
                  AND o.id LIKE 'personal-%'
                LIMIT 1
            )
            WHERE EXISTS (
                SELECT 1
                FROM users u
                JOIN orgs o ON o.login = u.github_login
                WHERE o.id = stacks.org_id
                  AND o.id LIKE 'personal-%'
            );";
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            DELETE FROM orgs
            WHERE id LIKE 'personal-%'
              AND login IN (SELECT github_login FROM users)
              AND NOT EXISTS (SELECT 1 FROM org_memberships m WHERE m.org_id = orgs.id)
              AND NOT EXISTS (SELECT 1 FROM projects p WHERE p.org_id = orgs.id)
              AND NOT EXISTS (SELECT 1 FROM stacks s WHERE s.org_id = orgs.id);";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task MigrateOwnerLoginCollisionsAsync(SqliteConnection connection)
    {
        var collisions = new List<(string OrgId, string Login)>();

        using (var find = connection.CreateCommand())
        {
            find.CommandText = @"
                SELECT o.id, o.login
                FROM orgs o
                JOIN users u ON o.login = u.github_login COLLATE NOCASE;";
            using var reader = await find.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                collisions.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        foreach (var collision in collisions)
        {
            using var hasRefs = connection.CreateCommand();
            hasRefs.CommandText = @"
                SELECT 1
                FROM (
                    SELECT 1 FROM org_memberships WHERE org_id = @orgId
                    UNION ALL
                    SELECT 1 FROM projects WHERE org_id = @orgId
                    UNION ALL
                    SELECT 1 FROM stacks WHERE org_id = @orgId
                    UNION ALL
                    SELECT 1 FROM api_tokens WHERE org_id = @orgId
                )
                LIMIT 1;";
            hasRefs.Parameters.AddWithValue("@orgId", collision.OrgId);
            var hasData = await hasRefs.ExecuteScalarAsync();

            if (hasData is not null)
            {
                throw new InvalidOperationException(
                    $"Owner login collision detected for '{collision.Login}'. " +
                    "User and organization names must be globally unique.");
            }

            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM orgs WHERE id = @orgId;";
            delete.Parameters.AddWithValue("@orgId", collision.OrgId);
            await delete.ExecuteNonQueryAsync();
        }
    }

    private static async Task MigrateOwnerLoginConstraintsAsync(SqliteConnection connection)
    {
        var commands = new[]
        {
            "CREATE UNIQUE INDEX IF NOT EXISTS idx_users_github_login_nocase ON users(github_login COLLATE NOCASE);",
            "CREATE UNIQUE INDEX IF NOT EXISTS idx_orgs_login_nocase ON orgs(login COLLATE NOCASE);",
            @"
                CREATE TRIGGER IF NOT EXISTS trg_users_owner_login_insert
                BEFORE INSERT ON users
                FOR EACH ROW
                WHEN EXISTS (
                    SELECT 1
                    FROM orgs o
                    WHERE o.login = NEW.github_login COLLATE NOCASE
                )
                BEGIN
                    SELECT RAISE(ABORT, 'owner login already exists');
                END;",
            @"
                CREATE TRIGGER IF NOT EXISTS trg_users_owner_login_update
                BEFORE UPDATE OF github_login ON users
                FOR EACH ROW
                WHEN EXISTS (
                    SELECT 1
                    FROM orgs o
                    WHERE o.login = NEW.github_login COLLATE NOCASE
                )
                BEGIN
                    SELECT RAISE(ABORT, 'owner login already exists');
                END;",
            @"
                CREATE TRIGGER IF NOT EXISTS trg_orgs_owner_login_insert
                BEFORE INSERT ON orgs
                FOR EACH ROW
                WHEN EXISTS (
                    SELECT 1
                    FROM users u
                    WHERE u.github_login = NEW.login COLLATE NOCASE
                )
                BEGIN
                    SELECT RAISE(ABORT, 'owner login already exists');
                END;",
            @"
                CREATE TRIGGER IF NOT EXISTS trg_orgs_owner_login_update
                BEFORE UPDATE OF login ON orgs
                FOR EACH ROW
                WHEN EXISTS (
                    SELECT 1
                    FROM users u
                    WHERE u.github_login = NEW.login COLLATE NOCASE
                )
                BEGIN
                    SELECT RAISE(ABORT, 'owner login already exists');
                END;"
        };

        foreach (var sql in commands)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
