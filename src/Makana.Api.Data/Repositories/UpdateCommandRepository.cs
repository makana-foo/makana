using Dapper;

namespace Makana.Api.Storage;

public sealed class UpdateCommandRepository(
    SqliteControlDatabase database, 
    StackShardDatabase stackShardDatabase,
    EphemeralStateCleanupPolicy cleanupPolicy)
{
    public string CreateUpdate(string stackId, string kind, string programRequestJson)
    {
        var updateId = Guid.NewGuid().ToString("D");
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        using var tx = connection.BeginImmediateTransaction();
        var sequence = connection.QuerySingle<int>(
            "SELECT COALESCE(MAX(sequence), 0) + 1 FROM updates WHERE stack_id = @stackId;",
            new { stackId },
            tx);

        connection.Execute(
            """
            INSERT INTO updates (id, stack_id, sequence, kind, status, version, program_request_json, created_at)
            VALUES (@id, @stackId, @sequence, @kind, @status, @version, @program, @created);
            """,
            new
            {
                id = updateId,
                stackId,
                sequence,
                kind,
                status = "not-started",
                version = 0,
                program = programRequestJson,
                created = DateTimeOffset.UtcNow.ToString("O")
            },
            tx);
        tx.Commit();
        StorageRepositoryCommon.RegisterUpdateLocation(database, updateId, stackId);
        return updateId;
    }

    public int GetCurrentStackVersion(string stackId)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        return UpdateRepositoryCommon.GetCurrentStackVersion(connection, transaction: null, stackId);
    }

    public int GetNextStackVersion(string stackId)
        => GetCurrentStackVersion(stackId) + 1;

    public StartUpdateResult StartUpdate(string updateId, string stackId, string startRequestJson)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        using var tx = connection.BeginImmediateTransaction();
        var update = connection.QuerySingleOrDefault<UpdateLookupRow>(
            """
            SELECT
                stack_id AS StackId,
                kind AS Kind,
                status AS Status,
                program_request_json AS ProgramRequestJson
            FROM updates
            WHERE id = @id
            LIMIT 1;
            """,
            new { id = updateId },
            tx);
        if (update is null || !string.Equals(update.StackId, stackId, StringComparison.Ordinal))
        {
            return new StartUpdateResult(UpdateStartStatus.NotFound, 0, null);
        }

        if (!string.Equals(update.Status, "not-started", StringComparison.Ordinal))
        {
            var state = string.Equals(update.Status, "in-progress", StringComparison.Ordinal)
                ? UpdateStartStatus.Conflict
                : UpdateStartStatus.InvalidState;
            return new StartUpdateResult(state, 0, null);
        }

        var startUpdateNow = DateTimeOffset.UtcNow;
        var graceCutoff = startUpdateNow - cleanupPolicy.ExpiredUpdateGracePeriod;
        connection.Execute(
            """
            UPDATE updates
            SET status = 'canceled',
                completed_at = @completedAt
            WHERE stack_id = @stackId
              AND status = 'in-progress'
              AND id != @updateId
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
                  WHERE t.update_id = updates.id
                    AND t.expires_at > @now
              );
            """,
            new
            {
                completedAt = startUpdateNow.ToString("O"),
                stackId,
                updateId,
                graceCutoff = graceCutoff.ToString("O"),
                now = startUpdateNow.ToString("O")
            },
            tx);

        var active = connection.QuerySingleOrDefault<int?>(
            """
            SELECT 1
            FROM updates
            WHERE stack_id = @stackId
              AND status = 'in-progress'
              AND id != @updateId
            LIMIT 1;
            """,
            new { stackId, updateId },
            tx);
        if (active is not null)
        {
            return new StartUpdateResult(UpdateStartStatus.Conflict, 0, null);
        }

        var currentVersion = UpdateRepositoryCommon.GetCurrentStackVersion(connection, tx, stackId);
        var assignedVersion = UpdateRepositoryCommon.IsPreviewOperation(update.Kind, update.ProgramRequestJson)
            ? currentVersion
            : currentVersion + 1;
        connection.Execute(
            """
            UPDATE updates
            SET status = @status, version = @version, start_request_json = @start
            WHERE id = @id;
            """,
            new
            {
                status = "in-progress",
                version = assignedVersion,
                start = startRequestJson,
                id = updateId
            },
            tx);

        var token = Guid.NewGuid().ToString("N");
        var leaseCreatedAt = DateTimeOffset.UtcNow;
        connection.Execute(
            """
            INSERT INTO update_tokens (token, update_id, expires_at, created_at)
            VALUES (@token, @updateId, @expiresAt, @createdAt);
            """,
            new
            {
                token,
                updateId,
                expiresAt = leaseCreatedAt.Add(cleanupPolicy.InitialUpdateLeaseDuration).ToString("O"),
                createdAt = leaseCreatedAt.ToString("O")
            },
            tx);
        tx.Commit();
        return new StartUpdateResult(UpdateStartStatus.Started, assignedVersion, token);
    }

    public bool IsUpdateInProgress(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return false;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        return connection.QuerySingleOrDefault<int?>(
            "SELECT 1 FROM updates WHERE id = @id AND status = 'in-progress' LIMIT 1;",
            new { id = updateId }) is not null;
    }

    public bool ValidateUpdateToken(string updateId, string token)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return false;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        return connection.QuerySingleOrDefault<int?>(
            """
            SELECT 1
            FROM update_tokens
            WHERE token = @token
              AND update_id = @updateId
              AND expires_at > @now
            LIMIT 1;
            """,
            new
            {
                token,
                updateId,
                now = DateTimeOffset.UtcNow.ToString("O")
            }) is not null;
    }

    public long? RenewUpdateTokenLease(string updateId, string token, int durationSeconds)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return null;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        var expiration = DateTimeOffset.UtcNow.AddSeconds(durationSeconds);
        var changed = connection.Execute(
            """
            UPDATE update_tokens
            SET expires_at = @expiresAt
            WHERE token = @token
              AND update_id = @updateId
              AND expires_at > @now;
            """,
            new
            {
                expiresAt = expiration.ToString("O"),
                token,
                updateId,
                now = DateTimeOffset.UtcNow.ToString("O")
            });
        return changed > 0 ? expiration.ToUnixTimeSeconds() : null;
    }

    public void CompleteUpdate(string updateId, string status, int? version = null)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        if (version is null)
        {
            connection.Execute(
                "UPDATE updates SET status = @status, completed_at = @completed WHERE id = @id;",
                new { status, completed = DateTimeOffset.UtcNow.ToString("O"), id = updateId });
            return;
        }

        connection.Execute(
            "UPDATE updates SET status = @status, version = @version, completed_at = @completed WHERE id = @id;",
            new { status, version, completed = DateTimeOffset.UtcNow.ToString("O"), id = updateId });
    }

    public void CancelUpdate(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        connection.Execute(
            "UPDATE updates SET status = @status, completed_at = @completed WHERE id = @id;",
            new { status = "canceled", completed = DateTimeOffset.UtcNow.ToString("O"), id = updateId });
    }

    public (string StackId, string Kind, int Version)? GetUpdateContext(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return null;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        var row = connection.QuerySingleOrDefault<UpdateContextRow>(
            """
            SELECT
                stack_id AS StackId,
                kind AS Kind,
                version AS Version,
                NULL AS ProgramRequestJson
            FROM updates
            WHERE id = @id
            LIMIT 1;
            """,
            new { id = updateId });
        return row is null ? null : (row.StackId, row.Kind, row.Version);
    }

    public UpdatePersistenceContext? GetUpdatePersistenceContext(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return null;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        var row = connection.QuerySingleOrDefault<UpdateContextRow>(
            """
            SELECT
                stack_id AS StackId,
                kind AS Kind,
                version AS Version,
                program_request_json AS ProgramRequestJson
            FROM updates
            WHERE id = @id
            LIMIT 1;
            """,
            new { id = updateId });
        return row is null
            ? null
            : new UpdatePersistenceContext(row.StackId, row.Kind, row.Version, row.ProgramRequestJson);
    }
}
