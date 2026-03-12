using Dapper;

namespace Makana.Api.Storage;

public sealed class DeploymentRunRepository(SqliteControlDatabase database, StackShardDatabase stackShardDatabase)
{
    public string? GetDeploymentSettings(string stackId)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        return connection.QuerySingleOrDefault<string>(
            "SELECT settings_json FROM deployment_settings WHERE stack_id = @stackId LIMIT 1;",
            new { stackId });
    }

    public void SaveDeploymentSettings(string stackId, string settingsJson)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        connection.Execute(
            """
            INSERT OR REPLACE INTO deployment_settings (stack_id, settings_json, updated_at)
            VALUES (@stackId, @settings, @updated);
            """,
            new
            {
                stackId,
                settings = settingsJson,
                updated = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    public void DeleteDeploymentSettings(string stackId)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        connection.Execute("DELETE FROM deployment_settings WHERE stack_id = @stackId;", new { stackId });
    }

    public string CreateDeploymentRun(string stackId, string requestJson)
    {
        var id = Guid.NewGuid().ToString("N");
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        connection.Execute(
            """
            INSERT INTO deployment_runs (id, stack_id, request_json, created_at)
            VALUES (@id, @stackId, @request, @created);
            """,
            new
            {
                id,
                stackId,
                request = requestJson,
                created = DateTimeOffset.UtcNow.ToString("O")
            });
        StorageRepositoryCommon.RegisterDeploymentRunLocation(database, id, stackId);
        return id;
    }

    public bool DeploymentRunExists(string deploymentId)
    {
        var location = StorageRepositoryCommon.ResolveDeploymentRunStorage(database, deploymentId);
        if (location is null)
        {
            return false;
        }

        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location.Value);
        return connection.QuerySingleOrDefault<int?>(
            "SELECT 1 FROM deployment_runs WHERE id = @id LIMIT 1;",
            new { id = deploymentId }) is not null;
    }
}
