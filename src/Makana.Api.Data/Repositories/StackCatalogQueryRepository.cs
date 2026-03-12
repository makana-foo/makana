using System.Text.Json;
using Dapper;
using Makana.Api.Contracts;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Storage;

public sealed class StackCatalogQueryRepository(SqliteControlDatabase database, StackShardDatabase stackShardDatabase)
{
    public List<(string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>
        ListStacksByNamespaceIds(string? projectFilter, IReadOnlyCollection<string> namespaceIds)
    {
        var ids = namespaceIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var namespacePredicate = StorageRepositoryCommon.AddNamespaceFilterParameters(command, ids);
        command.CommandText = $"""
            SELECT
                s.id,
                s.name,
                COALESCE(o.login, u.github_login),
                p.name,
                s.tags_json
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE ({namespacePredicate})
              AND (@project IS NULL OR p.name = @project)
            ORDER BY COALESCE(o.login, u.github_login), p.name, s.name;
            """;
        command.Parameters.AddWithValue("@project", (object?)projectFilter ?? DBNull.Value);

        var rows = new List<(string, string, string, string, string?, string?)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                null,
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    public List<(string NamespaceId, string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>
        ListStacksByNamespaceIdsDetailed(string? projectFilter, IReadOnlyCollection<string> namespaceIds)
    {
        var ids = namespaceIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        var namespacePredicate = StorageRepositoryCommon.AddNamespaceFilterParameters(command, ids);
        command.CommandText = $"""
            SELECT
                s.org_id,
                s.id,
                s.name,
                COALESCE(o.login, u.github_login),
                p.name,
                s.tags_json
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE ({namespacePredicate})
              AND (@project IS NULL OR p.name = @project)
            ORDER BY COALESCE(o.login, u.github_login), p.name, s.name;
            """;
        command.Parameters.AddWithValue("@project", (object?)projectFilter ?? DBNull.Value);

        var rows = new List<(string, string, string, string, string, string?, string?)>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                null,
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    public bool ProjectExistsByNamespaceId(string namespaceId, string project)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<int?>(
            """
            SELECT 1
            FROM projects
            WHERE org_id = @namespaceId AND name = @project
            LIMIT 1;
            """,
            new { namespaceId, project }) is not null;
    }

    public (string StackId, string ProjectId, string OrgId)? GetStackIdsByNamespaceId(string namespaceId, string project, string stack)
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<StackIdentityRow>(
            """
            SELECT
                s.id AS StackId,
                p.id AS ProjectId,
                s.org_id AS OrgId
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            WHERE s.org_id = @namespaceId AND p.name = @project AND s.name = @stack
            LIMIT 1;
            """,
            new { namespaceId, project, stack });
        return row is null ? null : (row.StackId, row.ProjectId, row.OrgId);
    }

    public (string? TagsJson, string? ConfigJson)? GetStackDataByNamespaceId(string namespaceId, string project, string stack)
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<StackDataRow>(
            """
            SELECT
                s.tags_json AS TagsJson,
                s.config_json AS ConfigJson
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            WHERE s.org_id = @namespaceId AND p.name = @project AND s.name = @stack
            LIMIT 1;
            """,
            new { namespaceId, project, stack });
        return row is null ? null : (row.TagsJson, row.ConfigJson);
    }

    public (string StackId, string ProjectId, string OrgId)? GetStackIds(string org, string project, string stack)
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<StackIdentityRow>(
            """
            SELECT
                s.id AS StackId,
                p.id AS ProjectId,
                s.org_id AS OrgId
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            LIMIT 1;
            """,
            new { org, project, stack });
        return row is null ? null : (row.StackId, row.ProjectId, row.OrgId);
    }

    public string? GetActiveUpdateId(string stackId)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        return connection.QuerySingleOrDefault<string>(
            """
            SELECT id
            FROM updates
            WHERE stack_id = @stackId AND status = 'in-progress'
            ORDER BY COALESCE(sequence, 0) DESC, created_at DESC
            LIMIT 1;
            """,
            new { stackId });
    }

    public (string? TagsJson, string ActiveUpdate, int Version)? GetStackPageMetadataById(string stackId)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        var row = connection.QuerySingleOrDefault<ActiveUpdateMetadataRow>(
            """
            SELECT
                COALESCE((
                    SELECT u.id
                    FROM updates u
                    WHERE u.stack_id = @stackId AND u.status = 'in-progress'
                    ORDER BY COALESCE(u.sequence, 0) DESC, u.created_at DESC
                    LIMIT 1
                ), '') AS ActiveUpdate,
                COALESCE((
                    SELECT d.version
                    FROM deployments d
                    WHERE d.stack_id = @stackId
                    ORDER BY d.version DESC
                    LIMIT 1
                ), 0) AS Version
            """,
            new { stackId });
        return row is null
            ? null
            : (GetStackTagsById(stackId), row.ActiveUpdate, row.Version);
    }

    public string? GetLatestUpdateTime(string stackId)
    {
        var location = StorageRepositoryCommon.ResolveStackStorage(database, stackId);
        using var connection = StorageRepositoryCommon.OpenStackConnection(stackShardDatabase, location);
        return connection.QuerySingleOrDefault<string>(
            """
            SELECT created_at
            FROM updates
            WHERE stack_id = @stackId
            ORDER BY created_at DESC
            LIMIT 1;
            """,
            new { stackId });
    }

    public Dictionary<string, StackConfigValue> GetStackConfigByNamespaceId(string namespaceId, string project, string stack)
    {
        var data = GetStackDataByNamespaceId(namespaceId, project, stack);
        if (data is null || data.Value.ConfigJson is null)
        {
            return [];
        }

        return JsonSerializer.Deserialize(
                   data.Value.ConfigJson,
                   StorageJsonContext.Default.DictionaryStringStackConfigValue) ??
               [];
    }

    private string? GetStackTagsById(string stackId)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<string>(
            "SELECT tags_json FROM stacks WHERE id = @stackId LIMIT 1;",
            new { stackId });
    }
}
