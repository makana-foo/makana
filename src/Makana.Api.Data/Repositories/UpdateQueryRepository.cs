using System.Text.Json;
using Dapper;
using Makana.Api.Contracts;
using Makana.StackUpdates.Pulumi;

namespace Makana.Api.Storage;

public sealed class UpdateQueryRepository(SqliteControlDatabase database, StackShardDatabase stackShardDatabase)
{
    public (string Status, int Version)? GetUpdate(string updateId)
    {
        var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
        if (location is null)
        {
            return null;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        var row = connection.QuerySingleOrDefault<UpdateStatusVersionRow>(
            "SELECT status AS Status, version AS Version FROM updates WHERE id = @id LIMIT 1;",
            new { id = updateId });
        return row is null ? null : (row.Status, row.Version);
    }

    public (string Id, string StackId, string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson)?
        GetUpdateDetails(string stackId, string updateId)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        var row = connection.QuerySingleOrDefault<UpdateDetailsRow>(
            """
            SELECT
                id AS Id,
                stack_id AS StackId,
                kind AS Kind,
                status AS Status,
                version AS Version,
                created_at AS CreatedAt,
                completed_at AS CompletedAt,
                program_request_json AS ProgramRequestJson
            FROM updates
            WHERE stack_id = @stackId AND id = @updateId
            LIMIT 1;
            """,
            new { stackId, updateId });
        return row is null
            ? null
            : (row.Id, row.StackId, row.Kind, row.Status, row.Version, row.CreatedAt, row.CompletedAt, row.ProgramRequestJson);
    }

    public List<(string Id, string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson)>
        GetHistory(string stackId, bool includePreviews = false)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        var rows = connection.Query<UpdateDetailsRow>(
            """
            SELECT
                id AS Id,
                stack_id AS StackId,
                kind AS Kind,
                status AS Status,
                version AS Version,
                created_at AS CreatedAt,
                completed_at AS CompletedAt,
                program_request_json AS ProgramRequestJson
            FROM updates
            WHERE stack_id = @stackId
            ORDER BY version DESC, COALESCE(sequence, 0) DESC, created_at DESC;
            """,
            new { stackId });
        var list = new List<(string, string, string, int, string, string?, string?)>();
        foreach (var row in rows)
        {
            if (!includePreviews && UpdateRepositoryCommon.IsPreviewOperation(row.Kind, row.ProgramRequestJson))
            {
                continue;
            }

            list.Add((row.Id, row.Kind, row.Status, row.Version, row.CreatedAt, row.CompletedAt, row.ProgramRequestJson));
        }

        return list;
    }

    public Dictionary<string, bool> GetUpdatePreviewFlags(IReadOnlyCollection<string> updateIds)
    {
        var previewFlags = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (updateIds.Count == 0)
        {
            return previewFlags;
        }

        foreach (var updateId in updateIds)
        {
            var location = StorageRepositoryCommon.ResolveUpdateStorage(database, updateId);
            if (location is null)
            {
                previewFlags[updateId] = false;
                continue;
            }

            using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
            var eventJson = connection.QuerySingleOrDefault<string>(
                """
                SELECT event_json
                FROM update_engine_events
                WHERE update_id = @updateId
                  AND event_json LIKE '%"summaryEvent"%'
                ORDER BY engine_sequence DESC
                LIMIT 1;
                """,
                new { updateId });
            if (!string.IsNullOrWhiteSpace(eventJson))
            {
                try
                {
                    var envelope = JsonSerializer.Deserialize(eventJson, PulumiEngineEventJsonContext.Default.EngineEvent);
                    previewFlags[updateId] = envelope?.SummaryEvent?.IsPreview == true;
                    continue;
                }
                catch (JsonException)
                {
                }
            }

            var update = connection.QuerySingleOrDefault<UpdateContextRow>(
                """
                SELECT
                    NULL AS StackId,
                    kind AS Kind,
                    0 AS Version,
                    program_request_json AS ProgramRequestJson
                FROM updates
                WHERE id = @updateId
                LIMIT 1;
                """,
                new { updateId });
            previewFlags[updateId] = update is not null &&
                                     UpdateRepositoryCommon.IsPreviewOperation(update.Kind, update.ProgramRequestJson);
        }

        return previewFlags;
    }

    public (string Id, string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson)?
        GetUpdateByVersion(string stackId, int version)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        var rows = connection.Query<UpdateDetailsRow>(
            """
            SELECT
                id AS Id,
                stack_id AS StackId,
                kind AS Kind,
                status AS Status,
                version AS Version,
                created_at AS CreatedAt,
                completed_at AS CompletedAt,
                program_request_json AS ProgramRequestJson
            FROM updates
            WHERE stack_id = @stackId AND version = @version
            ORDER BY COALESCE(sequence, 0) DESC, created_at DESC;
            """,
            new { stackId, version });
        foreach (var row in rows)
        {
            if (UpdateRepositoryCommon.IsPreviewOperation(row.Kind, row.ProgramRequestJson))
            {
                continue;
            }

            return (row.Id, row.Kind, row.Status, row.Version, row.CreatedAt, row.CompletedAt, row.ProgramRequestJson);
        }

        return null;
    }

    public (int Version, string DeploymentJson)? GetLatestDeployment(string stackId)
    {
        var deployment = GetLatestDeploymentEnvelope(stackId);
        return deployment is null ? null : (deployment.Value.Version, deployment.Value.DeploymentJson);
    }

    public (int Version, int SchemaVersion, string DeploymentJson)? GetLatestDeploymentEnvelope(string stackId)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        var row = connection.QuerySingleOrDefault<DeploymentEnvelopeRow>(
            """
            SELECT
                version AS Version,
                schema_version AS SchemaVersion,
                deployment_json AS DeploymentJson
            FROM deployments
            WHERE stack_id = @stackId
            ORDER BY version DESC
            LIMIT 1;
            """,
            new { stackId });
        return row is null ? null : (row.Version, row.SchemaVersion, row.DeploymentJson);
    }

    public (int Version, string DeploymentJson)? GetDeploymentByVersion(string stackId, int version)
    {
        var deployment = GetDeploymentEnvelopeByVersion(stackId, version);
        return deployment is null ? null : (deployment.Value.Version, deployment.Value.DeploymentJson);
    }

    public (int Version, int SchemaVersion, string DeploymentJson)? GetDeploymentEnvelopeByVersion(string stackId, int version)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        var row = connection.QuerySingleOrDefault<DeploymentEnvelopeRow>(
            """
            SELECT
                version AS Version,
                schema_version AS SchemaVersion,
                deployment_json AS DeploymentJson
            FROM deployments
            WHERE stack_id = @stackId
              AND version = @version
            LIMIT 1;
            """,
            new { stackId, version });
        return row is null ? null : (row.Version, row.SchemaVersion, row.DeploymentJson);
    }
}
