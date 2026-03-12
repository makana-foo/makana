using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Makana.Api.Storage;

internal static class StorageRepositoryCommon
{
    public const string ActiveShardState = "active";

    public static SqliteConnection OpenStackConnection(StackShardDatabase stackShardDatabase, StackStorageLocation location)
        => stackShardDatabase.OpenConnection(location.ShardPath);

    public static StackCatalogEntry? GetStackCatalogEntry(SqliteControlDatabase database, string stackId)
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<StackCatalogRow>(
            """
            SELECT
                id AS Id,
                org_id AS OrgId,
                project_id AS ProjectId,
                shard_path AS ShardPath,
                shard_state AS ShardState
            FROM stacks
            WHERE id = @stackId
            LIMIT 1;
            """,
            new { stackId });

        return row switch
        {
            not null => new StackCatalogEntry(row.Id, row.OrgId, row.ProjectId, row.ShardPath, row.ShardState),
            _ => null
        };
    }

    public static StackStorageLocation ResolveStackStorage(SqliteControlDatabase database, string stackId)
    {
        var catalogEntry = GetStackCatalogEntry(database, stackId);
        if (catalogEntry is null)
        {
            throw new InvalidOperationException($"Stack '{stackId}' was not found.");
        }

        if (!catalogEntry.Value.HasActiveShard)
        {
            throw new InvalidOperationException($"Stack '{stackId}' does not have an active shard.");
        }

        return new StackStorageLocation(stackId, catalogEntry.Value.ShardPath!);
    }

    public static StackStorageLocation? ResolveUpdateStorage(SqliteControlDatabase database, string updateId)
    {
        using var connection = database.OpenConnection();
#pragma warning disable DAP038 // Value-type single row 'OrDefault' usage
        var row = connection.QuerySingleOrDefault<StackStorageLocation>(
            """
            SELECT
                s.id AS Id,
                s.shard_path AS ShardPath
            FROM update_locations ul
            JOIN stacks s ON s.id = ul.stack_id
            WHERE ul.update_id = @updateId
              AND s.shard_path IS NOT NULL
              AND s.shard_state = @activeState
            LIMIT 1;
            """,
            new { updateId, activeState = ActiveShardState });
#pragma warning restore DAP038 // Value-type single row 'OrDefault' usage
        return row switch
        {
            { ShardPath: not null } => row,
            _ => null
        };
    }

    public static StackStorageLocation? ResolveDeploymentRunStorage(SqliteControlDatabase database, string deploymentRunId)
    {
        using var connection = database.OpenConnection();
#pragma warning disable DAP038 // Value-type single row 'OrDefault' usage
        var row = connection.QuerySingleOrDefault<StackStorageLocation>(
            """
            SELECT
                s.id AS Id,
                s.shard_path AS ShardPath
            FROM deployment_run_locations dr
            JOIN stacks s ON s.id = dr.stack_id
            WHERE dr.deployment_run_id = @deploymentRunId
              AND s.shard_path IS NOT NULL
              AND s.shard_state = @activeState
            LIMIT 1;
            """,
            new { deploymentRunId, activeState = ActiveShardState });
#pragma warning restore DAP038 // Value-type single row 'OrDefault' usage
        return row switch
        {
            { ShardPath: not null } => row,
            _ => null
        };
    }

    public static void RegisterUpdateLocation(SqliteControlDatabase database, string updateId, string stackId)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT OR REPLACE INTO update_locations (update_id, stack_id, created_at)
            VALUES (@updateId, @stackId, @createdAt);
            """,
            new
            {
                updateId,
                stackId,
                createdAt = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    public static void RegisterDeploymentRunLocation(SqliteControlDatabase database, string deploymentRunId, string stackId)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT OR REPLACE INTO deployment_run_locations (deployment_run_id, stack_id, created_at)
            VALUES (@deploymentRunId, @stackId, @createdAt);
            """,
            new
            {
                deploymentRunId,
                stackId,
                createdAt = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    public static void EnsureShardProvisioned(
        SqliteControlDatabase database,
        StackShardDatabase stackShardDatabase,
        string stackId,
        string namespaceId)
    {
        var shardPath = stackShardDatabase.GetShardPath(namespaceId, stackId);
        stackShardDatabase.EnsureInitialized(shardPath);

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        connection.Execute(
            """
            UPDATE stacks
            SET shard_path = @shardPath,
                shard_state = @shardState,
                shard_migrated_at = COALESCE(shard_migrated_at, @migratedAt)
            WHERE id = @stackId;
            """,
            new
            {
                shardPath,
                shardState = ActiveShardState,
                migratedAt = DateTimeOffset.UtcNow.ToString("O"),
                stackId
            },
            transaction);
        transaction.Commit();
    }

    public static List<(StackStorageLocation Location, string Owner, string Project, string Stack)> ListShardedStacks(
        SqliteControlDatabase database,
        CancellationToken cancellationToken = default)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.id,
                s.shard_path,
                COALESCE(o.login, u.github_login),
                p.name,
                s.name
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE s.shard_path IS NOT NULL
              AND s.shard_state = @activeState
            ORDER BY s.id;
            """;
        command.Parameters.AddWithValue("@activeState", ActiveShardState);

        var rows = new List<(StackStorageLocation, string, string, string)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows.Add((
                new StackStorageLocation(reader.GetString(0), reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return rows;
    }

    public static List<ExpiredUpdateCandidate> GetExpiredUpdateCandidatesFromConnection(
        SqliteConnection connection,
        string stackId,
        string owner,
        string project,
        string stack,
        DateTimeOffset now,
        DateTimeOffset graceCutoff,
        int limit)
    {
        const string Query =
            """
            SELECT
                u.id,
                u.stack_id
            FROM updates u
            WHERE u.status = 'in-progress'
              AND u.stack_id = @stackId
              AND COALESCE(
                    (
                        SELECT MAX(t.expires_at)
                        FROM update_tokens t
                        WHERE t.update_id = u.id
                    ),
                    u.created_at
                  ) <= @graceCutoff
              AND NOT EXISTS (
                  SELECT 1
                  FROM update_tokens t
                  WHERE t.update_id = u.id
                    AND t.expires_at > @now
              )
            ORDER BY u.created_at ASC
            LIMIT @limit;
            """;

        var results = connection.Query<ExpiredUpdateCandidate>(Query, new
        {
            stackId,
            graceCutoff = graceCutoff.ToString("O"),
            now = now.ToString("O"),
            limit
        });

        return [.. results];
    }

    public static int DeleteExpiredUpdateTokensFromConnection(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateTimeOffset expiresBefore,
        int batchSize)
    {
        return connection.Execute(
            """
            DELETE FROM update_tokens
            WHERE token IN (
                SELECT token
                FROM update_tokens
                WHERE expires_at IS NOT NULL
                  AND expires_at < @expiresBefore
                ORDER BY expires_at ASC
                LIMIT @batchSize
            );
            """,
            new
            {
                expiresBefore = expiresBefore.ToString("O"),
                batchSize
            },
            transaction);
    }

    public static List<string> SelectOldDeploymentRunIdsFromConnection(
        SqliteConnection connection,
        DateTimeOffset createdBefore,
        int batchSize)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id
            FROM deployment_runs
            WHERE created_at < @createdBefore
            ORDER BY created_at ASC
            LIMIT @batchSize;
            """;
        command.Parameters.AddWithValue("@createdBefore", createdBefore.ToString("O"));
        command.Parameters.AddWithValue("@batchSize", batchSize);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    public static void LogShardMaintenanceFailure(ILogger? logger, string operation, string stackId, Exception exception)
    {
        logger?.LogWarning(
            exception,
            "Skipping shard maintenance operation {Operation} for stack {StackId} after shard access failure.",
            operation,
            stackId);
    }

    public static string AddNamespaceFilterParameters(SqliteCommand command, IReadOnlyList<string> namespaceIds)
    {
        var predicates = new List<string>(namespaceIds.Count);
        for (var i = 0; i < namespaceIds.Count; i++)
        {
            var parameterName = $"@ns{i}";
            predicates.Add($"s.org_id = {parameterName}");
            command.Parameters.AddWithValue(parameterName, namespaceIds[i]);
        }

        return string.Join(" OR ", predicates);
    }
}
