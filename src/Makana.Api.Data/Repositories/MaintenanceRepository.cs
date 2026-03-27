using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Makana.Api.Storage;

public sealed class MaintenanceRepository(
    SqliteControlDatabase database,
    StackShardDatabase stackShardDatabase,
    EphemeralStateCleanupPolicy ephemeralStateCleanupPolicy,
    ILogger<MaintenanceRepository>? logger = null)
{
    public IReadOnlyList<ExpiredUpdateCandidate> GetExpiredUpdateCandidates(
        string? stackId,
        DateTimeOffset now,
        DateTimeOffset graceCutoff,
        int limit)
    {
        if (!string.IsNullOrWhiteSpace(stackId))
        {
            var catalogEntry = StorageRepositoryCommon.GetStackCatalogEntry(database, stackId);
            if (catalogEntry is null || !catalogEntry.Value.HasActiveShard)
            {
                return [];
            }

            using var controlConnection = database.OpenConnection();
            using var metadataCommand = controlConnection.CreateCommand();
            metadataCommand.CommandText = """
                SELECT
                    COALESCE(o.login, u.github_login),
                    p.name,
                    s.name
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users u ON u.id = s.org_id
                WHERE s.id = @stackId
                LIMIT 1;
                """;
            metadataCommand.Parameters.AddWithValue("@stackId", stackId);
            using var metadataReader = metadataCommand.ExecuteReader();
            if (!metadataReader.Read())
            {
                return [];
            }

            using var shardConnection = StorageRepositoryCommon.OpenStackConnection(
                stackShardDatabase,
                new StackStorageLocation(stackId, catalogEntry.Value.ShardPath!));
            return StorageRepositoryCommon.GetExpiredUpdateCandidatesFromConnection(
                shardConnection,
                stackId,
                metadataReader.GetString(0),
                metadataReader.GetString(1),
                metadataReader.GetString(2),
                now,
                graceCutoff,
                limit);
        }

        var candidates = new List<ExpiredUpdateCandidate>();
        foreach (var shard in StorageRepositoryCommon.ListShardedStacks(database))
        {
            try
            {
                using var shardConnection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, shard.Location);
                var shardCandidates = StorageRepositoryCommon.GetExpiredUpdateCandidatesFromConnection(
                    shardConnection,
                    shard.Location.StackId,
                    shard.Owner,
                    shard.Project,
                    shard.Stack,
                    now,
                    graceCutoff,
                    limit - candidates.Count);
                candidates.AddRange(shardCandidates);
                if (candidates.Count >= limit)
                {
                    break;
                }
            }
            catch (Exception exception) when (exception is IOException or SqliteException or InvalidOperationException)
            {
                StorageRepositoryCommon.LogShardMaintenanceFailure(logger, "cancel-expired-updates", shard.Location.StackId, exception);
            }
        }

        return candidates;
    }

    public ExpiredUpdateCancellationResult CancelExpiredUpdateAndDeleteTokens(
        string updateId,
        DateTimeOffset now,
        DateTimeOffset graceCutoff)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return new ExpiredUpdateCancellationResult(false, null);
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        using var tx = connection.BeginImmediateTransaction();
        var changed = connection.Execute(
            """
            UPDATE updates
            SET status = 'canceled',
                completed_at = @completedAt
            WHERE id = @updateId
              AND status = 'in-progress'
              AND COALESCE(
                    (
                        SELECT MAX(t.expires_at)
                        FROM update_tokens t
                        WHERE t.update_id = updates.id
                    ),
                    created_at
                  ) <= @graceCutoff
              AND NOT EXISTS (
                  SELECT 1
                  FROM update_tokens t
                  WHERE t.update_id = @updateId
                    AND t.expires_at > @now
              );
            """,
            new
            {
                completedAt = now.ToString("O"),
                updateId,
                graceCutoff = graceCutoff.ToString("O"),
                now = now.ToString("O")
            },
            tx);
        if (changed <= 0)
        {
            tx.Rollback();
            return new ExpiredUpdateCancellationResult(false, null);
        }

        connection.Execute("DELETE FROM update_tokens WHERE update_id = @updateId;", new { updateId }, tx);
        var latestSequence = connection.QuerySingleOrDefault<long?>(
            "SELECT MAX(engine_sequence) FROM update_engine_events WHERE update_id = @updateId;",
            new { updateId },
            tx);
        tx.Commit();
        return new ExpiredUpdateCancellationResult(true, latestSequence);
    }

    public (int apiTokens, int updateTokens, int uploads, int registryOperations, int deploymentRuns) PruneTransientState()
    {
        var now = DateTimeOffset.UtcNow;
        var batchSize = ephemeralStateCleanupPolicy.CleanupBatchSize;

        //TODO: these should all be enlisted in the same transaction

        var apiTokens = DeleteExpiredApiTokens(
                now - ephemeralStateCleanupPolicy.ExpiredApiTokenRetention,
                batchSize);

        var updateTokens = DeleteExpiredUpdateTokens(
                now - ephemeralStateCleanupPolicy.ExpiredUpdateTokenRetention,
                batchSize);

        var uploads = DeleteOldUploads(
                now - ephemeralStateCleanupPolicy.UploadRetention,
                batchSize);

        var registryOperations = DeleteOldRegistryOperations(
                now - ephemeralStateCleanupPolicy.RegistryOperationRetention,
                batchSize);

        var deploymentRuns = DeleteOldDeploymentRuns(
                now - ephemeralStateCleanupPolicy.DeploymentRunRetention,
                batchSize);

        return (apiTokens, updateTokens, uploads, registryOperations, deploymentRuns);
    }

    public int DeleteExpiredApiTokens(DateTimeOffset expiresBefore, int batchSize)
    {
        using var connection = database.OpenConnection();
        using var tx = connection.BeginImmediateTransaction();
        var deleted = connection.Execute(
            """
            DELETE FROM api_tokens
            WHERE token IN (
                SELECT token
                FROM api_tokens
                WHERE expires_at IS NOT NULL
                  AND expires_at < @expiresBefore
                ORDER BY expires_at ASC
                LIMIT @batchSize
            );
            """,
            new { expiresBefore = expiresBefore.ToString("O"), batchSize },
            tx);
        tx.Commit();
        return deleted;
    }

    public int DeleteExpiredUpdateTokens(DateTimeOffset expiresBefore, int batchSize, CancellationToken cancellationToken = default)
    {
        var deleted = 0;
        foreach (var shard in StorageRepositoryCommon.ListShardedStacks(database, cancellationToken))
        {
            if (deleted >= batchSize)
            {
                break;
            }

            try
            {
                using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, shard.Location);
                using var tx = connection.BeginImmediateTransaction();
                deleted += StorageRepositoryCommon.DeleteExpiredUpdateTokensFromConnection(
                    connection,
                    tx,
                    expiresBefore,
                    batchSize - deleted);
                tx.Commit();
            }
            catch (Exception exception) when (exception is IOException or SqliteException or InvalidOperationException)
            {
                StorageRepositoryCommon.LogShardMaintenanceFailure(logger, "delete-expired-update-tokens", shard.Location.StackId, exception);
            }
        }

        return deleted;
    }

    public int DeleteOldUploads(DateTimeOffset createdBefore, int batchSize)
    {
        using var connection = database.OpenConnection();
        using var tx = connection.BeginImmediateTransaction();
        var deleted = connection.Execute(
            """
            DELETE FROM uploads
            WHERE id IN (
                SELECT id
                FROM uploads
                WHERE created_at < @createdBefore
                ORDER BY created_at ASC
                LIMIT @batchSize
            );
            """,
            new { createdBefore = createdBefore.ToString("O"), batchSize },
            tx);
        tx.Commit();
        return deleted;
    }

    public int DeleteOldRegistryOperations(DateTimeOffset cutoff, int batchSize)
    {
        using var connection = database.OpenConnection();
        using var tx = connection.BeginImmediateTransaction();
        var deleted = connection.Execute(
            """
            DELETE FROM registry_ops
            WHERE id IN (
                SELECT id
                FROM registry_ops
                WHERE (completed_at IS NOT NULL AND completed_at < @cutoff)
                   OR (completed_at IS NULL AND created_at < @cutoff)
                ORDER BY CASE
                    WHEN completed_at IS NULL THEN created_at
                    ELSE completed_at
                END ASC
                LIMIT @batchSize
            );
            """,
            new { cutoff = cutoff.ToString("O"), batchSize },
            tx);
        tx.Commit();
        return deleted;
    }

    public int DeleteOldDeploymentRuns(DateTimeOffset createdBefore, int batchSize, CancellationToken cancellationToken = default)
    {
        var deletedRunIds = new List<string>();
        foreach (var shard in StorageRepositoryCommon.ListShardedStacks(database, cancellationToken))
        {
            if (deletedRunIds.Count >= batchSize)
            {
                break;
            }

            try
            {
                using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, shard.Location);
                using var transaction = connection.BeginImmediateTransaction();
                var shardRunIds = StorageRepositoryCommon.SelectOldDeploymentRunIdsFromConnection(
                    connection,
                    createdBefore,
                    batchSize - deletedRunIds.Count);
                if (shardRunIds.Count == 0)
                {
                    continue;
                }

                foreach (var runId in shardRunIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    connection.Execute("DELETE FROM deployment_runs WHERE id = @id;", new { id = runId }, transaction);
                    deletedRunIds.Add(runId);
                }

                transaction.Commit();
            }
            catch (Exception exception) when (exception is IOException or SqliteException or InvalidOperationException)
            {
                StorageRepositoryCommon.LogShardMaintenanceFailure(logger, "delete-old-deployment-runs", shard.Location.StackId, exception);
            }
        }

        if (deletedRunIds.Count == 0)
        {
            return 0;
        }

        using var controlConnection = database.OpenConnection();
        using var controlTransaction = controlConnection.BeginImmediateTransaction();
        foreach (var runId in deletedRunIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            controlConnection.Execute(
                "DELETE FROM deployment_run_locations WHERE deployment_run_id = @deploymentRunId;",
                new { deploymentRunId = runId },
                controlTransaction);
        }

        controlTransaction.Commit();
        return deletedRunIds.Count;
    }
}
