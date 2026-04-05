using System.Text.Json;
using Dapper;
using Makana.Journaling.Checkpointing;
using Makana.StackUpdates.Pulumi;

namespace Makana.Api.Storage;

public sealed class UpdateLogRepository(
    SqliteControlDatabase database,
    StackShardDatabase stackShardDatabase,
    UpdateCommandRepository updateCommands)
{
    public void SaveCheckpoint(string updateId, string deploymentJson, int version, int schemaVersion = DeploymentSchemaVersions.Current)
    {
        var normalizedDeploymentJson = DeploymentSnapshotNormalizer.NormalizeForPersistence(deploymentJson);
        var updateContext = updateCommands.GetUpdatePersistenceContext(updateId);
        if (updateContext is null)
        {
            return;
        }

        var location = StorageRepositoryCommon.ResolveStackStorage(database, updateContext.Value.StackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        using var transaction = connection.BeginImmediateTransaction();
        var isPreview = UpdateRepositoryCommon.IsPreviewOperation(updateContext.Value.Kind, updateContext.Value.ProgramRequestJson);
        var deploymentVersion = updateContext.Value.Version > 0 ? updateContext.Value.Version : version;
        if (!isPreview && deploymentVersion <= 0)
        {
            return;
        }

        var sequence = connection.QuerySingle<int>(
            "SELECT COALESCE(MAX(sequence), 0) FROM update_checkpoints WHERE update_id = @updateId;",
            new { updateId },
            transaction) + 1;

        connection.Execute(
            """
            INSERT INTO update_checkpoints (
                id,
                update_id,
                sequence,
                version,
                schema_version,
                deployment_json,
                request_signature,
                created_at)
            VALUES (@id, @updateId, @sequence, @version, @schemaVersion, @deployment, @signature, @created);
            """,
            new
            {
                id = Guid.NewGuid().ToString("N"),
                updateId,
                sequence,
                version = deploymentVersion,
                schemaVersion,
                deployment = normalizedDeploymentJson,
                signature = (string?)null,
                created = DateTimeOffset.UtcNow.ToString("O")
            },
            transaction);
        if (isPreview)
        {
            transaction.Commit();
            return;
        }

        connection.Execute(
            """
            INSERT OR REPLACE INTO deployments (id, stack_id, version, schema_version, deployment_json, created_at)
            VALUES (@depId, @stackId, @version, @schemaVersion, @deployment, @created);
            """,
            new
            {
                depId = Guid.NewGuid().ToString("N"),
                stackId = updateContext.Value.StackId,
                version = deploymentVersion,
                schemaVersion,
                deployment = normalizedDeploymentJson,
                created = DateTimeOffset.UtcNow.ToString("O")
            },
            transaction);
        transaction.Commit();
    }

    public bool SaveCheckpointWithSequence(
        string updateId,
        int sequence,
        string deploymentJson,
        int version,
        string requestSignature,
        int schemaVersion = DeploymentSchemaVersions.Current)
    {
        var updateContext = updateCommands.GetUpdatePersistenceContext(updateId);
        if (updateContext is null)
        {
            return false;
        }

        var location = StorageRepositoryCommon.ResolveStackStorage(database, updateContext.Value.StackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        using var tx = connection.BeginImmediateTransaction();
        var isPreview = UpdateRepositoryCommon.IsPreviewOperation(updateContext.Value.Kind, updateContext.Value.ProgramRequestJson);
        var deploymentVersion = updateContext.Value.Version > 0 ? updateContext.Value.Version : version;
        if (!isPreview && deploymentVersion <= 0)
        {
            return false;
        }

        connection.Execute(
            """
            INSERT INTO update_checkpoints (
                id,
                update_id,
                sequence,
                version,
                schema_version,
                deployment_json,
                request_signature,
                created_at)
            VALUES (@id, @updateId, @sequence, @version, @schemaVersion, @deployment, @signature, @created);
            """,
            new
            {
                id = Guid.NewGuid().ToString("N"),
                updateId,
                sequence,
                version = deploymentVersion,
                schemaVersion,
                deployment = deploymentJson,
                signature = requestSignature,
                created = DateTimeOffset.UtcNow.ToString("O")
            },
            tx);
        if (isPreview)
        {
            tx.Commit();
            return true;
        }

        connection.Execute(
            """
            INSERT OR REPLACE INTO deployments (id, stack_id, version, schema_version, deployment_json, created_at)
            VALUES (@depId, @stackId, @version, @schemaVersion, @deployment, @created);
            """,
            new
            {
                depId = Guid.NewGuid().ToString("N"),
                stackId = updateContext.Value.StackId,
                version = deploymentVersion,
                schemaVersion,
                deployment = deploymentJson,
                created = DateTimeOffset.UtcNow.ToString("O")
            },
            tx);
        tx.Commit();
        return true;
    }

    public StoredUpdateCheckpoint? GetCheckpointBySequenceForUpdate(string updateId, int sequence)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return null;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        var row = connection.QuerySingleOrDefault<StoredUpdateCheckpointRow>(
            """
            SELECT
                sequence AS Sequence,
                version AS Version,
                deployment_json AS DeploymentJson,
                request_signature AS RequestSignature
            FROM update_checkpoints
            WHERE update_id = @updateId AND sequence = @sequence
            LIMIT 1;
            """,
            new { updateId, sequence });
        return row is null ? null : new StoredUpdateCheckpoint(row.Sequence, row.Version, row.DeploymentJson, row.RequestSignature);
    }

    public StoredUpdateCheckpoint? GetLatestCheckpointForUpdate(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return null;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        var row = connection.QuerySingleOrDefault<StoredUpdateCheckpointRow>(
            """
            SELECT
                sequence AS Sequence,
                version AS Version,
                deployment_json AS DeploymentJson,
                request_signature AS RequestSignature
            FROM update_checkpoints
            WHERE update_id = @updateId
            ORDER BY sequence DESC
            LIMIT 1;
            """,
            new { updateId });
        return row is null ? null : new StoredUpdateCheckpoint(row.Sequence, row.Version, row.DeploymentJson, row.RequestSignature);
    }

    public bool RecordEvents(string updateId, IEnumerable<string> eventsJson)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return false;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        using var transaction = connection.BeginImmediateTransaction();
        foreach (var json in eventsJson)
        {
            var engineEvent = JsonSerializer.Deserialize(json, PulumiEngineEventJsonContext.Default.EngineEvent);
            if (engineEvent is null || !engineEvent.HasSinglePayload)
            {
                throw new InvalidOperationException("engine event must contain exactly one payload");
            }

            connection.Execute(
                """
                INSERT INTO update_engine_events (id, update_id, engine_sequence, event_json, created_at)
                VALUES (@id, @updateId, @sequence, @eventJson, @created);
                """,
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    updateId,
                    sequence = engineEvent.Sequence,
                    eventJson = json,
                    created = DateTimeOffset.UtcNow.ToString("O")
                },
                transaction);
        }

        transaction.Commit();
        return true;
    }

    public bool RecordJournalEntries(string updateId, IEnumerable<string> entriesJson)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return false;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        using var transaction = connection.BeginImmediateTransaction();
        var sequence = connection.QuerySingle<int>(
            "SELECT COALESCE(MAX(sequence), 0) FROM update_journal_entries WHERE update_id = @updateId;",
            new { updateId },
            transaction);

        foreach (var json in entriesJson)
        {
            sequence += 1;
            connection.Execute(
                """
                INSERT INTO update_journal_entries (id, update_id, sequence, entry_json, created_at)
                VALUES (@id, @updateId, @sequence, @entry, @created);
                """,
                new
                {
                    id = Guid.NewGuid().ToString("N"),
                    updateId,
                    sequence,
                    entry = json,
                    created = DateTimeOffset.UtcNow.ToString("O")
                },
                transaction);
        }

        transaction.Commit();
        return true;
    }

    public List<string> GetEvents(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return [];
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        return connection.Query<string>(
            "SELECT event_json FROM update_engine_events WHERE update_id = @updateId ORDER BY engine_sequence ASC;",
            new { updateId }) is var rows ? [.. rows] : [];
    }

    public StoredEngineEventPage GetEventPage(string updateId, long? afterSequence, int limit)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return new StoredEngineEventPage([], null);
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        var rows = connection.Query<EngineEventRow>(
            """
            SELECT
                engine_sequence AS EngineSequence,
                event_json AS EventJson
            FROM update_engine_events
            WHERE update_id = @updateId
              AND engine_sequence > @afterSequence
            ORDER BY engine_sequence ASC
            LIMIT @limit;
            """,
            new
            {
                updateId,
                afterSequence = afterSequence ?? 0,
                limit
            });
        return new StoredEngineEventPage([.. rows.Select(static row => row.EventJson)], rows.LastOrDefault()?.EngineSequence);
    }

    public long? GetLatestEventSequence(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return null;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        return connection.QuerySingleOrDefault<long?>(
            "SELECT MAX(engine_sequence) FROM update_engine_events WHERE update_id = @updateId;",
            new { updateId });
    }

    public List<string> GetJournalEntries(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return [];
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        return connection.Query<string>(
            "SELECT entry_json FROM update_journal_entries WHERE update_id = @updateId ORDER BY sequence ASC;",
            new { updateId }) is var rows ? [.. rows] : [];
    }
}
