using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Makana.Api.Contracts;
using Makana.Journaling.Checkpointing;
using Makana.StackUpdates.Pulumi;

namespace Makana.Api.Storage;

public sealed class BackendStore
{
    private readonly Database _database;

    public enum StartUpdateStatus
    {
        Started,
        NotFound,
        Conflict,
        InvalidState
    }

    public readonly record struct StartUpdateResult(StartUpdateStatus Status, int Version, string? Token);
    public readonly record struct StoredUpdateCheckpoint(int Sequence, int Version, string DeploymentJson, string? RequestSignature);
    public readonly record struct StoredEngineEventPage(IReadOnlyList<string> EventsJson, long? LastSequence);

    public BackendStore(Database database)
    {
        _database = database;
    }

    public async Task EnsureApiTokenAsync(string token, string userId, string? orgId, DateTimeOffset? expiresAt)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO api_tokens (token, user_id, org_id, expires_at)
            VALUES (@token, @user, @org, @expires)
            ON CONFLICT(token) DO UPDATE SET
                user_id = excluded.user_id,
                org_id = excluded.org_id,
                expires_at = excluded.expires_at;";
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@user", userId);
        cmd.Parameters.AddWithValue("@org", (object?)orgId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@expires", expiresAt?.ToString("O") ?? (object)DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<bool> IsApiTokenValidAsync(string token)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM api_tokens
            WHERE token = @token
              AND (expires_at IS NULL OR expires_at > @now)
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<(string UserId, string? OrgId)?> GetApiTokenOwnerAsync(string token)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT user_id, org_id
            FROM api_tokens
            WHERE token = @token
              AND (expires_at IS NULL OR expires_at > @now)
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1)
        );
    }

    public async Task<(string Id, string Login, string Name, string? Email, string AvatarUrl)?> GetUserByIdAsync(string userId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, github_login, display_name, email, avatar_url
            FROM users
            WHERE id = @userId
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@userId", userId);

        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? string.Empty : reader.GetString(4)
        );
    }

    public async Task<string?> GetUserIdByLoginAsync(string login)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM users WHERE github_login = @login LIMIT 1;";
        cmd.Parameters.AddWithValue("@login", login);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task<List<(string Id, string Login)>> GetUserOrgsAsync(string userId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT o.id, o.login
            FROM org_memberships m
            JOIN orgs o ON o.id = m.org_id
            WHERE m.user_id = @userId
            ORDER BY o.login;";
        cmd.Parameters.AddWithValue("@userId", userId);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<(string Id, string Login)>();
        while (await reader.ReadAsync())
        {
            results.Add((reader.GetString(0), reader.GetString(1)));
        }
        return results;
    }

    public async Task<bool> IsUserMemberOfOrgLoginAsync(string userId, string orgLogin)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM org_memberships m
            JOIN orgs o ON o.id = m.org_id
            WHERE m.user_id = @userId
              AND o.login = @orgLogin
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@orgLogin", orgLogin);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task<string?> GetOrgIdForMemberAsync(string userId, string orgLogin)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT o.id
            FROM org_memberships m
            JOIN orgs o ON o.id = m.org_id
            WHERE m.user_id = @userId
              AND o.login = @orgLogin
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@userId", userId);
        cmd.Parameters.AddWithValue("@orgLogin", orgLogin);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task EnsureUserAsync(
        string userId,
        string login,
        string displayName,
        string? email,
        string avatarUrl)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO users (id, github_login, display_name, email, avatar_url)
            VALUES (@id, @login, @displayName, @email, @avatar)
            ON CONFLICT(id) DO UPDATE SET
                github_login = excluded.github_login,
                display_name = excluded.display_name,
                email = excluded.email,
                avatar_url = excluded.avatar_url;";
        cmd.Parameters.AddWithValue("@id", userId);
        cmd.Parameters.AddWithValue("@login", login);
        cmd.Parameters.AddWithValue("@displayName", displayName);
        cmd.Parameters.AddWithValue("@email", (object?)email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@avatar", avatarUrl);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task EnsureOrgAsync(string orgId, string login)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO orgs (id, login)
            VALUES (@id, @login)
            ON CONFLICT(id) DO UPDATE SET
                login = excluded.login;";
        cmd.Parameters.AddWithValue("@id", orgId);
        cmd.Parameters.AddWithValue("@login", login);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetOrgIdByLoginAsync(string login)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM orgs WHERE login = @login LIMIT 1;";
        cmd.Parameters.AddWithValue("@login", login);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task<bool> OrgExistsByLoginAsync(string login)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM orgs WHERE login = @login LIMIT 1;";
        cmd.Parameters.AddWithValue("@login", login);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task<string?> GetOrgLoginByIdAsync(string orgId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT login FROM orgs WHERE id = @orgId LIMIT 1;";
        cmd.Parameters.AddWithValue("@orgId", orgId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task EnsureOrgMembershipAsync(string orgId, string userId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR IGNORE INTO org_memberships (org_id, user_id)
            VALUES (@orgId, @userId);";
        cmd.Parameters.AddWithValue("@orgId", orgId);
        cmd.Parameters.AddWithValue("@userId", userId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<(string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>> ListStacksAsync(
        string? projectFilter,
        string? orgFilter)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id, s.name, COALESCE(o.login, u.github_login), p.name, NULL as last_update, s.tags_json
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE (@org IS NULL OR COALESCE(o.login, u.github_login) = @org)
              AND (@project IS NULL OR p.name = @project)
            ORDER BY COALESCE(o.login, u.github_login), p.name, s.name;";
        cmd.Parameters.AddWithValue("@org", (object?)orgFilter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@project", (object?)projectFilter ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<(string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>();
        while (await reader.ReadAsync())
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)
            ));
        }
        return results;
    }

    public async Task<List<(string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>>
        ListStacksByNamespaceIdsAsync(string? projectFilter, IReadOnlyCollection<string> namespaceIds)
    {
        var ids = namespaceIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return new List<(string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>();
        }

        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();

        var namespacePredicateParts = new List<string>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            namespacePredicateParts.Add($"s.org_id = @ns{i}");
            cmd.Parameters.AddWithValue($"@ns{i}", ids[i]);
        }

        cmd.CommandText = $@"
            SELECT s.id, s.name, COALESCE(o.login, u.github_login), p.name, NULL as last_update, s.tags_json
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE ({string.Join(" OR ", namespacePredicateParts)})
              AND (@project IS NULL OR p.name = @project)
            ORDER BY COALESCE(o.login, u.github_login), p.name, s.name;";
        cmd.Parameters.AddWithValue("@project", (object?)projectFilter ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<(string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>();
        while (await reader.ReadAsync())
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)
            ));
        }

        return results;
    }

    public async Task<List<(string NamespaceId, string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>>
        ListStacksByNamespaceIdsDetailedAsync(string? projectFilter, IReadOnlyCollection<string> namespaceIds)
    {
        var ids = namespaceIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return new List<(string NamespaceId, string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>();
        }

        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();

        var namespacePredicateParts = new List<string>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            namespacePredicateParts.Add($"s.org_id = @ns{i}");
            cmd.Parameters.AddWithValue($"@ns{i}", ids[i]);
        }

        cmd.CommandText = $@"
            SELECT s.org_id, s.id, s.name, COALESCE(o.login, u.github_login), p.name, NULL as last_update, s.tags_json
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE ({string.Join(" OR ", namespacePredicateParts)})
              AND (@project IS NULL OR p.name = @project)
            ORDER BY COALESCE(o.login, u.github_login), p.name, s.name;";
        cmd.Parameters.AddWithValue("@project", (object?)projectFilter ?? DBNull.Value);

        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<(string NamespaceId, string StackId, string Name, string OrgName, string ProjectName, string? LastUpdate, string? TagsJson)>();
        while (await reader.ReadAsync())
        {
            results.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)
            ));
        }

        return results;
    }

    public async Task<bool> ProjectExistsByNamespaceIdAsync(string namespaceId, string project)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM projects
            WHERE org_id = @namespaceId AND name = @project
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@namespaceId", namespaceId);
        cmd.Parameters.AddWithValue("@project", project);
        var result = await cmd.ExecuteScalarAsync();
        return result is not null;
    }

    public async Task<(string StackId, string ProjectId, string OrgId)?> GetStackIdsByNamespaceIdAsync(
        string namespaceId, string project, string stack)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id, p.id, s.org_id
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            WHERE s.org_id = @namespaceId AND p.name = @project AND s.name = @stack
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@namespaceId", namespaceId);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }
        return null;
    }

    public async Task<string> EnsureProjectByNamespaceIdAsync(string namespaceId, string project)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id FROM projects WHERE org_id = @namespaceId AND name = @project LIMIT 1;";
        cmd.Parameters.AddWithValue("@namespaceId", namespaceId);
        cmd.Parameters.AddWithValue("@project", project);
        var existing = (string?)await cmd.ExecuteScalarAsync();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var projectId = Guid.NewGuid().ToString("N");
        cmd.CommandText = "INSERT INTO projects (id, org_id, name) VALUES (@id, @namespaceId, @name);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", projectId);
        cmd.Parameters.AddWithValue("@namespaceId", namespaceId);
        cmd.Parameters.AddWithValue("@name", project);
        await cmd.ExecuteNonQueryAsync();
        return projectId;
    }

    public async Task<string> CreateStackByNamespaceIdAsync(
        string namespaceId, string project, string stack, string? tagsJson, string? configJson)
    {
        var projectId = await EnsureProjectByNamespaceIdAsync(namespaceId, project);
        var stackId = Guid.NewGuid().ToString("N");

        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO stacks (id, org_id, project_id, name, tags_json, config_json)
            VALUES (@id, @namespaceId, @projectId, @name, @tags, @config);
        ";
        cmd.Parameters.AddWithValue("@id", stackId);
        cmd.Parameters.AddWithValue("@namespaceId", namespaceId);
        cmd.Parameters.AddWithValue("@projectId", projectId);
        cmd.Parameters.AddWithValue("@name", stack);
        cmd.Parameters.AddWithValue("@tags", (object?)tagsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@config", (object?)configJson ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return stackId;
    }

    public async Task SaveInitialDeploymentForStackAsync(
        string stackId,
        int version,
        int schemaVersion,
        string deploymentJson,
        CancellationToken cancellationToken = default)
    {
        var normalizedDeploymentJson = DeploymentSnapshotNormalizer.NormalizeForPersistence(deploymentJson);

        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO deployments (id, stack_id, version, schema_version, deployment_json, created_at)
            VALUES (@id, @stackId, @version, @schemaVersion, @deployment, @created);";
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@schemaVersion", schemaVersion);
        cmd.Parameters.AddWithValue("@deployment", normalizedDeploymentJson);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(string? TagsJson, string? ConfigJson)?> GetStackDataByNamespaceIdAsync(
        string namespaceId, string project, string stack)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT s.tags_json, s.config_json
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            WHERE s.org_id = @namespaceId AND p.name = @project AND s.name = @stack
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@namespaceId", namespaceId);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)
            );
        }
        return null;
    }

    public async Task<bool> ProjectExistsAsync(string org, string project)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM projects p
            LEFT JOIN orgs o ON o.id = p.org_id
            LEFT JOIN users u ON u.id = p.org_id
            WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<(string StackId, string ProjectId, string OrgId)?> GetStackIdsAsync(string org, string project, string stack)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT s.id, p.id, s.org_id
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        }
        return null;
    }

    public async Task<string?> GetActiveUpdateIdAsync(string stackId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id
            FROM updates
            WHERE stack_id = @stackId AND status = 'in-progress'
            ORDER BY COALESCE(sequence, 0) DESC, created_at DESC
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task<(string? TagsJson, string ActiveUpdate, int Version)?> GetStackPageMetadataByIdAsync(string stackId, CancellationToken cancellationToken)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT
                s.tags_json,
                COALESCE((
                    SELECT u.id
                    FROM updates u
                    WHERE u.stack_id = s.id AND u.status = 'in-progress'
                    ORDER BY COALESCE(u.sequence, 0) DESC, u.created_at DESC
                    LIMIT 1
                ), '') AS active_update,
                COALESCE((
                    SELECT d.version
                    FROM deployments d
                    WHERE d.stack_id = s.id
                    ORDER BY d.version DESC
                    LIMIT 1
                ), 0) AS version
            FROM stacks s
            WHERE s.id = @stackId
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2));
        }

        return null;
    }

    public async Task<string> EnsureProjectAsync(string org, string project)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT p.id
            FROM projects p
            LEFT JOIN orgs o ON o.id = p.org_id
            LEFT JOIN users u ON u.id = p.org_id
            WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        var existing = (string?)await cmd.ExecuteScalarAsync();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        cmd.CommandText = "SELECT id FROM orgs WHERE login = @org LIMIT 1;";
        var namespaceId = (string?)await cmd.ExecuteScalarAsync();
        if (string.IsNullOrWhiteSpace(namespaceId))
        {
            cmd.CommandText = "SELECT id FROM users WHERE github_login = @org LIMIT 1;";
            namespaceId = (string?)await cmd.ExecuteScalarAsync();
        }

        if (string.IsNullOrWhiteSpace(namespaceId))
        {
            throw new InvalidOperationException("Organization not found");
        }

        var projectId = Guid.NewGuid().ToString("N");
        cmd.CommandText = "INSERT INTO projects (id, org_id, name) VALUES (@id, @orgId, @name);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", projectId);
        cmd.Parameters.AddWithValue("@orgId", namespaceId);
        cmd.Parameters.AddWithValue("@name", project);
        await cmd.ExecuteNonQueryAsync();
        return projectId;
    }

    public async Task<string> CreateStackAsync(string org, string project, string stack, string? tagsJson, string? configJson)
    {
        var projectId = await EnsureProjectAsync(org, project);
        var stackId = Guid.NewGuid().ToString("N");

        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT org_id FROM projects WHERE id = @projectId LIMIT 1;";
        cmd.Parameters.AddWithValue("@projectId", projectId);
        var namespaceId = (string?)await cmd.ExecuteScalarAsync();
        if (string.IsNullOrWhiteSpace(namespaceId))
        {
            throw new InvalidOperationException("Organization not found");
        }

        cmd.CommandText = @"
            INSERT INTO stacks (id, org_id, project_id, name, tags_json, config_json)
            VALUES (@id, @orgId, @projectId, @name, @tags, @config);
        ";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", stackId);
        cmd.Parameters.AddWithValue("@orgId", namespaceId);
        cmd.Parameters.AddWithValue("@projectId", projectId);
        cmd.Parameters.AddWithValue("@name", stack);
        cmd.Parameters.AddWithValue("@tags", (object?)tagsJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@config", (object?)configJson ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
        return stackId;
    }

    public async Task<(string? TagsJson, string? ConfigJson)?> GetStackDataAsync(string org, string project, string stack)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT s.tags_json, s.config_json
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);

        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1)
            );
        }
        return null;
    }

    public async Task UpdateTagsAsync(string org, string project, string stack, string tagsJson)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE stacks
            SET tags_json = @tags
            WHERE id IN (
                SELECT s.id
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users u ON u.id = s.org_id
                WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.AddWithValue("@tags", tagsJson);
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task UpdateConfigAsync(string org, string project, string stack, string configJson)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE stacks
            SET config_json = @config
            WHERE id IN (
                SELECT s.id
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users u ON u.id = s.org_id
                WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.AddWithValue("@config", configJson);
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task RenameStackAsync(
        string org,
        string project,
        string stack,
        string newName,
        string? newProject,
        CancellationToken cancellationToken = default)
    {
        var targetProject = string.IsNullOrWhiteSpace(newProject) ? project : newProject!;
        var projectId = await EnsureProjectAsync(org, targetProject);

        using var connection = _database.OpenConnection();
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;

        cmd.CommandText = @"
            SELECT s.id
            FROM stacks s
            JOIN projects p ON p.id = s.project_id
            LEFT JOIN orgs o ON o.id = s.org_id
            LEFT JOIN users u ON u.id = s.org_id
            WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        var stackId = (string?)await cmd.ExecuteScalarAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(stackId))
        {
            await tx.RollbackAsync(cancellationToken);
            return;
        }

        var deployments = new List<(string Id, string DeploymentJson)>();
        cmd.CommandText = @"
            SELECT id, deployment_json
            FROM deployments
            WHERE stack_id = @stackId;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@stackId", stackId);
        using (var deploymentReader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await deploymentReader.ReadAsync(cancellationToken))
            {
                deployments.Add((deploymentReader.GetString(0), deploymentReader.GetString(1)));
            }
        }

        var checkpoints = new List<(string Id, string DeploymentJson)>();
        cmd.CommandText = @"
            SELECT c.id, c.deployment_json
            FROM update_checkpoints c
            JOIN updates u ON u.id = c.update_id
            WHERE u.stack_id = @stackId;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@stackId", stackId);
        using (var checkpointReader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await checkpointReader.ReadAsync(cancellationToken))
            {
                checkpoints.Add((checkpointReader.GetString(0), checkpointReader.GetString(1)));
            }
        }

        cmd.CommandText = @"
            UPDATE stacks
            SET name = @newName, project_id = @projectId
            WHERE id IN (
                SELECT s.id
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users u ON u.id = s.org_id
                WHERE COALESCE(o.login, u.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@newName", newName);
        cmd.Parameters.AddWithValue("@projectId", projectId);
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        foreach (var deployment in deployments)
        {
            var rewritten = RewriteDeploymentForRenamedStack(
                deployment.DeploymentJson,
                project,
                stack,
                targetProject,
                newName);

            cmd.CommandText = "UPDATE deployments SET deployment_json = @deploymentJson WHERE id = @id;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@deploymentJson", rewritten);
            cmd.Parameters.AddWithValue("@id", deployment.Id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var checkpoint in checkpoints)
        {
            var rewritten = RewriteDeploymentForRenamedStack(
                checkpoint.DeploymentJson,
                project,
                stack,
                targetProject,
                newName);

            cmd.CommandText = "UPDATE update_checkpoints SET deployment_json = @deploymentJson WHERE id = @id;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@deploymentJson", rewritten);
            cmd.Parameters.AddWithValue("@id", checkpoint.Id);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }

    public string RewriteImportedDeploymentForTargetStack(
        string deploymentJson,
        string targetOwner,
        string targetProject,
        string targetStack)
    {
        var root = JsonNode.Parse(deploymentJson) as JsonObject;
        if (root is null)
        {
            return deploymentJson;
        }

        RewriteImportedSecretsProviderState(root, targetOwner, targetProject, targetStack);
        return root.ToJsonString();
    }

    private static string RewriteDeploymentForRenamedStack(
        string deploymentJson,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        var root = JsonNode.Parse(deploymentJson) as JsonObject;
        if (root is null)
        {
            return deploymentJson;
        }

        RewriteResourceArray(root["resources"] as JsonArray, oldProject, oldStack, newProject, newStack);

        if (root["pendingOperations"] is JsonArray pendingOperations)
        {
            RewritePendingOperations(pendingOperations, oldProject, oldStack, newProject, newStack);
        }

        if (root["pending_operations"] is JsonArray legacyPendingOperations)
        {
            RewritePendingOperations(legacyPendingOperations, oldProject, oldStack, newProject, newStack);
        }

        RewriteSecretsProviderState(root, oldProject, oldStack, newProject, newStack);
        return root.ToJsonString();
    }

    private static void RewriteResourceArray(
        JsonArray? resources,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        if (resources is null)
        {
            return;
        }

        foreach (var node in resources)
        {
            if (node is JsonObject resource)
            {
                RewriteResourceObject(resource, oldProject, oldStack, newProject, newStack);
            }
        }
    }

    private static void RewritePendingOperations(
        JsonArray operations,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        foreach (var node in operations)
        {
            if (node is not JsonObject operation ||
                operation["resource"] is not JsonObject resource)
            {
                continue;
            }

            RewriteResourceObject(resource, oldProject, oldStack, newProject, newStack);
        }
    }

    private static void RewriteResourceObject(
        JsonObject resource,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        RewriteStringProperty(resource, "urn", value => RewriteUrn(value, oldProject, oldStack, newProject, newStack));
        RewriteStringProperty(resource, "parent", value => RewriteUrn(value, oldProject, oldStack, newProject, newStack));
        RewriteStringProperty(resource, "deletedWith", value => RewriteUrn(value, oldProject, oldStack, newProject, newStack));
        RewriteStringProperty(resource, "provider", value => RewriteProviderReference(value, oldProject, oldStack, newProject, newStack));

        RewriteUrnArray(resource["dependencies"] as JsonArray, oldProject, oldStack, newProject, newStack);
        RewriteUrnArray(resource["replaceWith"] as JsonArray, oldProject, oldStack, newProject, newStack);

        if (resource["propertyDependencies"] is JsonObject propertyDependencies)
        {
            foreach (var dependencyList in propertyDependencies)
            {
                RewriteUrnArray(dependencyList.Value as JsonArray, oldProject, oldStack, newProject, newStack);
            }
        }
    }

    private static void RewriteUrnArray(
        JsonArray? values,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        if (values is null)
        {
            return;
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] is JsonValue value && value.TryGetValue<string>(out var urn))
            {
                values[i] = RewriteUrn(urn, oldProject, oldStack, newProject, newStack);
            }
        }
    }

    private static void RewriteStringProperty(JsonObject node, string propertyName, Func<string, string> rewrite)
    {
        if (node[propertyName] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
        {
            node[propertyName] = rewrite(text);
        }
    }

    private static string RewriteProviderReference(
        string providerReference,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        var separator = providerReference.LastIndexOf("::", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return RewriteUrn(providerReference, oldProject, oldStack, newProject, newStack);
        }

        var urn = providerReference[..separator];
        var id = providerReference[(separator + 2)..];
        var rewrittenUrn = RewriteUrn(urn, oldProject, oldStack, newProject, newStack);
        return rewrittenUrn == urn ? providerReference : $"{rewrittenUrn}::{id}";
    }

    private static string RewriteUrn(
        string urn,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        const string prefix = "urn:pulumi:";
        if (!urn.StartsWith(prefix, StringComparison.Ordinal))
        {
            return urn;
        }

        var stackSeparator = urn.IndexOf("::", prefix.Length, StringComparison.Ordinal);
        if (stackSeparator < 0)
        {
            return urn;
        }

        var projectSeparator = urn.IndexOf("::", stackSeparator + 2, StringComparison.Ordinal);
        if (projectSeparator < 0)
        {
            return urn;
        }

        var typeSeparator = urn.IndexOf("::", projectSeparator + 2, StringComparison.Ordinal);
        if (typeSeparator < 0)
        {
            return urn;
        }

        var stackName = urn[prefix.Length..stackSeparator];
        var projectName = urn[(stackSeparator + 2)..projectSeparator];
        if (!string.Equals(stackName, oldStack, StringComparison.Ordinal) ||
            !string.Equals(projectName, oldProject, StringComparison.Ordinal))
        {
            return urn;
        }

        var resourceType = urn[(projectSeparator + 2)..typeSeparator];
        var resourceName = urn[(typeSeparator + 2)..];
        if (string.Equals(resourceType, "pulumi:pulumi:Stack", StringComparison.Ordinal))
        {
            resourceName = $"{newProject}-{newStack}";
        }

        return $"{prefix}{newStack}::{newProject}::{resourceType}::{resourceName}";
    }

    private static void RewriteSecretsProviderState(
        JsonObject root,
        string oldProject,
        string oldStack,
        string newProject,
        string newStack)
    {
        var state = GetSecretsProviderState(root);
        if (state?["url"] is not JsonValue value || !value.TryGetValue<string>(out var url))
        {
            return;
        }

        var oldSegment = $"/project/{oldProject}/stack/{oldStack}";
        var newSegment = $"/project/{newProject}/stack/{newStack}";
        if (url.Contains(oldSegment, StringComparison.Ordinal))
        {
            state["url"] = url.Replace(oldSegment, newSegment, StringComparison.Ordinal);
        }
    }

    private static void RewriteImportedSecretsProviderState(
        JsonObject root,
        string targetOwner,
        string targetProject,
        string targetStack)
    {
        var secretsProviders = root["secrets_providers"] as JsonObject ?? root["secretsProviders"] as JsonObject;
        if (secretsProviders is null)
        {
            return;
        }

        var type = secretsProviders["type"]?.GetValue<string>();
        if (!string.Equals(type, "service", StringComparison.Ordinal))
        {
            return;
        }

        var state = GetSecretsProviderState(root);
        if (state is null)
        {
            return;
        }

        state["owner"] = targetOwner;
        state["project"] = targetProject;
        state["stack"] = targetStack;
    }

    private static JsonObject? GetSecretsProviderState(JsonObject root)
    {
        var secretsProviders = root["secrets_providers"] as JsonObject ?? root["secretsProviders"] as JsonObject;
        return secretsProviders?["state"] as JsonObject;
    }

    public async Task DeleteStackAsync(string org, string project, string stack)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM update_checkpoints
            WHERE update_id IN (
                SELECT u.id
                FROM updates u
                JOIN stacks s ON s.id = u.stack_id
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users us ON us.id = s.org_id
                WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            DELETE FROM update_events
            WHERE update_id IN (
                SELECT u.id
                FROM updates u
                JOIN stacks s ON s.id = u.stack_id
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users us ON us.id = s.org_id
                WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            DELETE FROM update_engine_events
            WHERE update_id IN (
                SELECT u.id
                FROM updates u
                JOIN stacks s ON s.id = u.stack_id
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users us ON us.id = s.org_id
                WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            DELETE FROM update_journal_entries
            WHERE update_id IN (
                SELECT u.id
                FROM updates u
                JOIN stacks s ON s.id = u.stack_id
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users us ON us.id = s.org_id
                WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            DELETE FROM update_tokens
            WHERE update_id IN (
                SELECT u.id
                FROM updates u
                JOIN stacks s ON s.id = u.stack_id
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users us ON us.id = s.org_id
                WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            DELETE FROM deployments
            WHERE stack_id IN (
                SELECT s.id
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users us ON us.id = s.org_id
                WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            DELETE FROM updates
            WHERE stack_id IN (
                SELECT s.id
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users us ON us.id = s.org_id
                WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            DELETE FROM stacks
            WHERE id IN (
                SELECT s.id
                FROM stacks s
                JOIN projects p ON p.id = s.project_id
                LEFT JOIN orgs o ON o.id = s.org_id
                LEFT JOIN users us ON us.id = s.org_id
                WHERE COALESCE(o.login, us.github_login) = @org AND p.name = @project AND s.name = @stack
            );";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string> CreateUpdateAsync(string stackId, string kind, string programRequestJson)
    {
        var updateId = Guid.NewGuid().ToString("D");
        using var connection = _database.OpenConnection();
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(MAX(sequence), 0) + 1 FROM updates WHERE stack_id = @stackId;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        var sequence = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        cmd.CommandText = @"
            INSERT INTO updates (id, stack_id, sequence, kind, status, version, program_request_json, created_at)
            VALUES (@id, @stackId, @sequence, @kind, @status, @version, @program, @created);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", updateId);
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@sequence", sequence);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@status", "not-started");
        cmd.Parameters.AddWithValue("@version", 0);
        cmd.Parameters.AddWithValue("@program", programRequestJson);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return updateId;
    }

    private static bool IsPreviewKind(string kind) =>
        string.Equals(kind, "preview", StringComparison.OrdinalIgnoreCase);

    private static bool? TryGetPreviewProgramRequest(string? programRequestJson)
    {
        if (string.IsNullOrWhiteSpace(programRequestJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(programRequestJson);
            if (!doc.RootElement.TryGetProperty("options", out var optionsElement) ||
                optionsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (optionsElement.TryGetProperty("dryRun", out var dryRunElement) &&
                dryRunElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return dryRunElement.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static bool IsPreviewOperation(string kind, string? programRequestJson)
    {
        var dryRun = TryGetPreviewProgramRequest(programRequestJson);
        return dryRun ?? IsPreviewKind(kind);
    }

    private static async Task<int> GetCurrentStackVersionAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string stackId)
    {
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            SELECT version, kind, program_request_json
            FROM updates
            WHERE stack_id = @stackId
              AND version > 0
            UNION ALL
            SELECT version, 'deployment', NULL
            FROM deployments
            WHERE stack_id = @stackId;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        using var reader = await cmd.ExecuteReaderAsync();
        var currentVersion = 0;
        while (await reader.ReadAsync())
        {
            var version = reader.GetInt32(0);
            var kind = reader.GetString(1);
            var programRequestJson = reader.IsDBNull(2) ? null : reader.GetString(2);
            if (string.Equals(kind, "deployment", StringComparison.OrdinalIgnoreCase) ||
                !IsPreviewOperation(kind, programRequestJson))
            {
                currentVersion = Math.Max(currentVersion, version);
            }
        }

        return currentVersion;
    }

    public async Task<int> GetCurrentStackVersionAsync(string stackId)
    {
        using var connection = _database.OpenConnection();
        return await GetCurrentStackVersionAsync(connection, transaction: null, stackId);
    }

    public async Task<int> GetNextStackVersionAsync(string stackId)
    {
        var current = await GetCurrentStackVersionAsync(stackId);
        return current + 1;
    }

    public async Task<StartUpdateResult> StartUpdateAsync(string updateId, string stackId, string startRequestJson)
    {
        using var connection = _database.OpenConnection();
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT stack_id, kind, status, program_request_json
            FROM updates
            WHERE id = @id
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return new StartUpdateResult(StartUpdateStatus.NotFound, 0, null);
        }

        var updateStackId = reader.GetString(0);
        var updateKind = reader.GetString(1);
        var currentStatus = reader.GetString(2);
        var programRequestJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        reader.Close();
        if (!string.Equals(updateStackId, stackId, StringComparison.Ordinal))
        {
            return new StartUpdateResult(StartUpdateStatus.NotFound, 0, null);
        }

        if (!string.Equals(currentStatus, "not-started", StringComparison.Ordinal))
        {
            var state = string.Equals(currentStatus, "in-progress", StringComparison.Ordinal)
                ? StartUpdateStatus.Conflict
                : StartUpdateStatus.InvalidState;
            return new StartUpdateResult(state, 0, null);
        }

        cmd.CommandText = @"
            SELECT 1
            FROM updates
            WHERE stack_id = @stackId
              AND status = 'in-progress'
              AND id != @updateId
            LIMIT 1;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var active = await cmd.ExecuteScalarAsync();
        if (active is not null)
        {
            return new StartUpdateResult(StartUpdateStatus.Conflict, 0, null);
        }

        var currentVersion = await GetCurrentStackVersionAsync(connection, tx, stackId);
        var assignedVersion = IsPreviewOperation(updateKind, programRequestJson) ? currentVersion : currentVersion + 1;
        cmd.CommandText = @"
            UPDATE updates
            SET status = @status, version = @version, start_request_json = @start
            WHERE id = @id;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@status", "in-progress");
        cmd.Parameters.AddWithValue("@version", assignedVersion);
        cmd.Parameters.AddWithValue("@start", startRequestJson);
        cmd.Parameters.AddWithValue("@id", updateId);
        await cmd.ExecuteNonQueryAsync();

        var token = Guid.NewGuid().ToString("N");
        cmd.CommandText = "INSERT INTO update_tokens (token, update_id, expires_at) VALUES (@token, @updateId, NULL);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@updateId", updateId);
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
        return new StartUpdateResult(StartUpdateStatus.Started, assignedVersion, token);
    }

    public async Task<bool> HasActiveUpdateAsync(string stackId, string updateId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT 1
            FROM updates
            WHERE stack_id = @stackId
              AND status = 'in-progress'
              AND id != @updateId
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    public async Task<bool> IsUpdateInProgressAsync(string updateId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM updates WHERE id = @id AND status = 'in-progress' LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    public async Task<bool> ValidateUpdateTokenAsync(string updateId, string token, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT 1
            FROM update_tokens
            WHERE token = @token
              AND update_id = @updateId
              AND (expires_at IS NULL OR expires_at > @now)
            LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@updateId", updateId);
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result != null;
    }

    public async Task<long?> RenewUpdateTokenLeaseAsync(
        string updateId,
        string token,
        int durationSeconds,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();

        var expiration = DateTimeOffset.UtcNow.AddSeconds(durationSeconds);
        cmd.CommandText = """
            UPDATE update_tokens
            SET expires_at = @expiresAt
            WHERE token = @token
              AND update_id = @updateId;
            """;
        cmd.Parameters.AddWithValue("@expiresAt", expiration.ToString("O"));
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var changed = await cmd.ExecuteNonQueryAsync(cancellationToken);
        return changed > 0 ? expiration.ToUnixTimeSeconds() : null;
    }

    public async Task SaveCheckpointAsync(
        string updateId,
        string deploymentJson,
        int version,
        CancellationToken cancellationToken = default,
        int schemaVersion = DeploymentSchemaVersions.Current)
    {
        var normalizedDeploymentJson = DeploymentSnapshotNormalizer.NormalizeForPersistence(deploymentJson);

        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT stack_id, kind, version, program_request_json FROM updates WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return;
        }

        var stackId = reader.GetString(0);
        var updateKind = reader.GetString(1);
        var updateVersion = reader.GetInt32(2);
        var programRequestJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        reader.Close();
        var isPreview = IsPreviewOperation(updateKind, programRequestJson);
        var deploymentVersion = updateVersion > 0 ? updateVersion : version;
        if (!isPreview && deploymentVersion <= 0)
        {
            return;
        }

        cmd.CommandText = "SELECT COALESCE(MAX(sequence), 0) FROM update_checkpoints WHERE update_id = @updateId;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var sequence = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) + 1;

        cmd.CommandText = @"
            INSERT INTO update_checkpoints (
                id,
                update_id,
                sequence,
                version,
                schema_version,
                deployment_json,
                request_signature,
                created_at)
            VALUES (@id, @updateId, @sequence, @version, @schemaVersion, @deployment, @signature, @created);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@updateId", updateId);
        cmd.Parameters.AddWithValue("@sequence", sequence);
        cmd.Parameters.AddWithValue("@version", deploymentVersion);
        cmd.Parameters.AddWithValue("@schemaVersion", schemaVersion);
        cmd.Parameters.AddWithValue("@deployment", normalizedDeploymentJson);
        cmd.Parameters.AddWithValue("@signature", DBNull.Value);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (isPreview)
        {
            return;
        }

        cmd.CommandText = @"
            INSERT OR REPLACE INTO deployments (id, stack_id, version, schema_version, deployment_json, created_at)
            VALUES (@depId, @stackId, @version, @schemaVersion, @deployment, @created);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@depId", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@version", deploymentVersion);
        cmd.Parameters.AddWithValue("@schemaVersion", schemaVersion);
        cmd.Parameters.AddWithValue("@deployment", normalizedDeploymentJson);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> SaveCheckpointWithSequenceAsync(
        string updateId,
        int sequence,
        string deploymentJson,
        int version,
        string requestSignature,
        CancellationToken cancellationToken = default,
        int schemaVersion = DeploymentSchemaVersions.Current)
    {
        var normalizedDeploymentJson = DeploymentSnapshotNormalizer.NormalizeForPersistence(deploymentJson);

        using var connection = _database.OpenConnection();
        using var tx = connection.BeginTransaction();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT stack_id, kind, version, program_request_json FROM updates WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return false;
        }

        var stackId = reader.GetString(0);
        var updateKind = reader.GetString(1);
        var updateVersion = reader.GetInt32(2);
        var programRequestJson = reader.IsDBNull(3) ? null : reader.GetString(3);
        reader.Close();
        var isPreview = IsPreviewOperation(updateKind, programRequestJson);
        var deploymentVersion = updateVersion > 0 ? updateVersion : version;
        if (!isPreview && deploymentVersion <= 0)
        {
            return false;
        }

        cmd.CommandText = @"
            INSERT INTO update_checkpoints (
                id,
                update_id,
                sequence,
                version,
                schema_version,
                deployment_json,
                request_signature,
                created_at)
            VALUES (@id, @updateId, @sequence, @version, @schemaVersion, @deployment, @signature, @created);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@updateId", updateId);
        cmd.Parameters.AddWithValue("@sequence", sequence);
        cmd.Parameters.AddWithValue("@version", deploymentVersion);
        cmd.Parameters.AddWithValue("@schemaVersion", schemaVersion);
        cmd.Parameters.AddWithValue("@deployment", normalizedDeploymentJson);
        cmd.Parameters.AddWithValue("@signature", requestSignature);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (isPreview)
        {
            await tx.CommitAsync(cancellationToken);
            return true;
        }

        cmd.CommandText = @"
            INSERT OR REPLACE INTO deployments (id, stack_id, version, schema_version, deployment_json, created_at)
            VALUES (@depId, @stackId, @version, @schemaVersion, @deployment, @created);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@depId", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@version", deploymentVersion);
        cmd.Parameters.AddWithValue("@schemaVersion", schemaVersion);
        cmd.Parameters.AddWithValue("@deployment", normalizedDeploymentJson);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<StoredUpdateCheckpoint?> GetCheckpointBySequenceForUpdateAsync(
        string updateId,
        int sequence,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT sequence, version, deployment_json, request_signature
            FROM update_checkpoints
            WHERE update_id = @updateId AND sequence = @sequence
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        cmd.Parameters.AddWithValue("@sequence", sequence);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredUpdateCheckpoint(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public async Task<StoredUpdateCheckpoint?> GetLatestCheckpointForUpdateAsync(
        string updateId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT sequence, version, deployment_json, request_signature
            FROM update_checkpoints
            WHERE update_id = @updateId
            ORDER BY sequence DESC
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredUpdateCheckpoint(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public async Task RecordEventsAsync(
        string updateId,
        IEnumerable<string> eventsJson,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();

        foreach (var json in eventsJson)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var engineEvent = JsonSerializer.Deserialize(json, PulumiEngineEventJsonContext.Default.EngineEvent);
            if (engineEvent is null || !engineEvent.HasSinglePayload)
            {
                throw new InvalidOperationException("engine event must contain exactly one payload");
            }

            cmd.CommandText = @"
                INSERT INTO update_engine_events (id, update_id, engine_sequence, event_json, created_at)
                VALUES (@id, @updateId, @sequence, @event, @created);";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@updateId", updateId);
            cmd.Parameters.AddWithValue("@sequence", engineEvent.Sequence);
            cmd.Parameters.AddWithValue("@event", json);
            cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task RecordJournalEntriesAsync(
        string updateId,
        IEnumerable<string> entriesJson,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(sequence), 0) FROM update_journal_entries WHERE update_id = @updateId;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var sequence = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));

        foreach (var json in entriesJson)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sequence += 1;
            cmd.CommandText = @"
                INSERT INTO update_journal_entries (id, update_id, sequence, entry_json, created_at)
                VALUES (@id, @updateId, @sequence, @entry, @created);";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@updateId", updateId);
            cmd.Parameters.AddWithValue("@sequence", sequence);
            cmd.Parameters.AddWithValue("@entry", json);
            cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<List<string>> GetEventsAsync(string updateId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT event_json FROM update_engine_events WHERE update_id = @updateId ORDER BY engine_sequence ASC;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetString(0));
        }
        return results;
    }

    public async Task<StoredEngineEventPage> GetEventPageAsync(
        string updateId,
        long? afterSequence,
        int limit,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT engine_sequence, event_json
            FROM update_engine_events
            WHERE update_id = @updateId
              AND engine_sequence > @afterSequence
            ORDER BY engine_sequence ASC
            LIMIT @limit;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        cmd.Parameters.AddWithValue("@afterSequence", afterSequence ?? 0);
        cmd.Parameters.AddWithValue("@limit", limit);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<string>();
        long? lastSequence = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            lastSequence = reader.GetInt64(0);
            results.Add(reader.GetString(1));
        }

        return new StoredEngineEventPage(results, lastSequence);
    }

    public async Task<long?> GetLatestEventSequenceAsync(string updateId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MAX(engine_sequence) FROM update_engine_events WHERE update_id = @updateId;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var scalar = await cmd.ExecuteScalarAsync(cancellationToken);
        return scalar is null or DBNull ? null : Convert.ToInt64(scalar);
    }

    public async Task<List<string>> GetJournalEntriesAsync(string updateId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT entry_json FROM update_journal_entries WHERE update_id = @updateId ORDER BY sequence ASC;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var results = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }

    public async Task CompleteUpdateAsync(string updateId, string status, int? version = null, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = version is null
            ? "UPDATE updates SET status = @status, completed_at = @completed WHERE id = @id;"
            : "UPDATE updates SET status = @status, version = @version, completed_at = @completed WHERE id = @id;";
        cmd.Parameters.AddWithValue("@status", status);
        if (version is not null)
        {
            cmd.Parameters.AddWithValue("@version", version.Value);
        }

        cmd.Parameters.AddWithValue("@completed", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", updateId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CancelUpdateAsync(string updateId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE updates SET status = @status, completed_at = @completed WHERE id = @id;";
        cmd.Parameters.AddWithValue("@status", "canceled");
        cmd.Parameters.AddWithValue("@completed", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", updateId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(string Status, int Version)?> GetUpdateAsync(string updateId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT status, version FROM updates WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (reader.GetString(0), reader.GetInt32(1));
        }
        return null;
    }

    public async Task<(string Id, string StackId, string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson)?>
        GetUpdateDetailsAsync(
            string stackId,
            string updateId,
            CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, stack_id, kind, status, version, created_at, completed_at, program_request_json
            FROM updates
            WHERE stack_id = @stackId AND id = @updateId
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@updateId", updateId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)
            );
        }

        return null;
    }

    public async Task<(string StackId, string Kind, int Version)?> GetUpdateContextAsync(
        string updateId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT stack_id, kind, version FROM updates WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2)
            );
        }
        return null;
    }

    public readonly record struct UpdatePersistenceContext(string StackId, string Kind, int Version, string? ProgramRequestJson);

    public async Task<UpdatePersistenceContext?> GetUpdatePersistenceContextAsync(
        string updateId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT stack_id, kind, version, program_request_json FROM updates WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new UpdatePersistenceContext(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }

        return null;
    }

    public async Task<List<(string Id, string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson)>>
        GetHistoryAsync(string stackId, bool includePreviews = false)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, kind, status, version, created_at, completed_at, program_request_json
            FROM updates
            WHERE stack_id = @stackId
            ORDER BY version DESC, COALESCE(sequence, 0) DESC, created_at DESC;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<(string, string, string, int, string, string?, string?)>();
        while (await reader.ReadAsync())
        {
            var kind = reader.GetString(1);
            var programRequestJson = reader.IsDBNull(6) ? null : reader.GetString(6);
            if (!includePreviews && IsPreviewOperation(kind, programRequestJson))
            {
                continue;
            }

            list.Add((
                reader.GetString(0),
                kind,
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                programRequestJson
            ));
        }
        return list;
    }

    public async Task<Dictionary<string, bool>> GetUpdatePreviewFlagsAsync(
        IReadOnlyCollection<string> updateIds,
        CancellationToken cancellationToken = default)
    {
        var previewFlags = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (updateIds.Count == 0)
        {
            return previewFlags;
        }

        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();

        var parameterNames = new List<string>(updateIds.Count);
        var index = 0;
        foreach (var updateId in updateIds)
        {
            var parameterName = $"@id{index++}";
            parameterNames.Add(parameterName);
            cmd.Parameters.AddWithValue(parameterName, updateId);
        }

        cmd.CommandText = $@"
            SELECT update_id, event_json
            FROM update_engine_events
            WHERE update_id IN ({string.Join(", ", parameterNames)})
              AND event_json LIKE '%""summaryEvent""%'
            ORDER BY update_id, engine_sequence DESC;";

        {
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var updateId = reader.GetString(0);
                if (previewFlags.ContainsKey(updateId))
                {
                    continue;
                }

                var eventJson = reader.GetString(1);
                try
                {
                    var envelope = JsonSerializer.Deserialize(eventJson, PulumiEngineEventJsonContext.Default.EngineEvent);
                    previewFlags[updateId] = envelope?.SummaryEvent?.IsPreview == true;
                }
                catch (JsonException)
                {
                    previewFlags[updateId] = false;
                }
            }
        }

        if (previewFlags.Count == updateIds.Count)
        {
            return previewFlags;
        }

        cmd.Parameters.Clear();
        parameterNames.Clear();
        index = 0;
        foreach (var updateId in updateIds)
        {
            if (previewFlags.ContainsKey(updateId))
            {
                continue;
            }

            var parameterName = $"@updateId{index++}";
            parameterNames.Add(parameterName);
            cmd.Parameters.AddWithValue(parameterName, updateId);
        }

        if (parameterNames.Count == 0)
        {
            return previewFlags;
        }

        cmd.CommandText = $@"
            SELECT id, kind, program_request_json
            FROM updates
            WHERE id IN ({string.Join(", ", parameterNames)});";

        using var updateReader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await updateReader.ReadAsync(cancellationToken))
        {
            var updateId = updateReader.GetString(0);
            var kind = updateReader.GetString(1);
            var programRequestJson = updateReader.IsDBNull(2) ? null : updateReader.GetString(2);
            previewFlags[updateId] = IsPreviewOperation(kind, programRequestJson);
        }

        return previewFlags;
    }

    public async Task<(string Id, string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson)?>
        GetUpdateByVersionAsync(string stackId, int version)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT id, kind, status, version, created_at, completed_at, program_request_json
            FROM updates
            WHERE stack_id = @stackId AND version = @version
            ORDER BY COALESCE(sequence, 0) DESC, created_at DESC;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@version", version);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var kind = reader.GetString(1);
            var programRequestJson = reader.IsDBNull(6) ? null : reader.GetString(6);
            if (IsPreviewOperation(kind, programRequestJson))
            {
                continue;
            }

            return (
                reader.GetString(0),
                kind,
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                programRequestJson
            );
        }
        return null;
    }

    public async Task<(int Version, string DeploymentJson)?> GetLatestDeploymentAsync(
        string stackId,
        CancellationToken cancellationToken = default)
    {
        var deployment = await GetLatestDeploymentEnvelopeAsync(stackId, cancellationToken);
        return deployment is null
            ? null
            : (deployment.Value.Version, deployment.Value.DeploymentJson);
    }

    public async Task<(int Version, int SchemaVersion, string DeploymentJson)?> GetLatestDeploymentEnvelopeAsync(
        string stackId,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT version, schema_version, deployment_json
            FROM deployments
            WHERE stack_id = @stackId
            ORDER BY version DESC
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2));
        }
        return null;
    }

    public async Task<(int Version, string DeploymentJson)?> GetDeploymentByVersionAsync(
        string stackId,
        int version,
        CancellationToken cancellationToken = default)
    {
        var deployment = await GetDeploymentEnvelopeByVersionAsync(stackId, version, cancellationToken);
        return deployment is null
            ? null
            : (deployment.Value.Version, deployment.Value.DeploymentJson);
    }

    public async Task<(int Version, int SchemaVersion, string DeploymentJson)?> GetDeploymentEnvelopeByVersionAsync(
        string stackId,
        int version,
        CancellationToken cancellationToken = default)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT version, schema_version, deployment_json
            FROM deployments
            WHERE stack_id = @stackId
              AND version = @version
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@version", version);
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return (reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2));
        }

        return null;
    }

    public async Task<string?> GetLatestUpdateTimeAsync(string stackId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT created_at
            FROM updates
            WHERE stack_id = @stackId
            ORDER BY created_at DESC
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task<Dictionary<string, StackConfigValue>> GetStackConfigAsync(string org, string project, string stack)
    {
        var data = await GetStackDataAsync(org, project, stack);
        if (data is null || data.Value.ConfigJson is null)
        {
            return new Dictionary<string, StackConfigValue>();
        }
        var config = JsonSerializer.Deserialize(
                data.Value.ConfigJson,
                StorageJsonContext.Default.DictionaryStringStackConfigValue) ??
            new Dictionary<string, StackConfigValue>();
        return config;
    }

    public async Task<Dictionary<string, StackConfigValue>> GetStackConfigByNamespaceIdAsync(
        string namespaceId, string project, string stack)
    {
        var data = await GetStackDataByNamespaceIdAsync(namespaceId, project, stack);
        if (data is null || data.Value.ConfigJson is null)
        {
            return new Dictionary<string, StackConfigValue>();
        }

        var config = JsonSerializer.Deserialize(
                data.Value.ConfigJson,
                StorageJsonContext.Default.DictionaryStringStackConfigValue) ??
            new Dictionary<string, StackConfigValue>();
        return config;
    }

    public async Task<string?> GetDeploymentSettingsAsync(string stackId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT settings_json FROM deployment_settings WHERE stack_id = @stackId LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task SaveDeploymentSettingsAsync(string stackId, string settingsJson)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO deployment_settings (stack_id, settings_json, updated_at)
            VALUES (@stackId, @settings, @updated);";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@settings", settingsJson);
        cmd.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteDeploymentSettingsAsync(string stackId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM deployment_settings WHERE stack_id = @stackId;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string> CreateDeploymentRunAsync(string stackId, string requestJson)
    {
        var id = Guid.NewGuid().ToString("N");
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO deployment_runs (id, stack_id, request_json, created_at)
            VALUES (@id, @stackId, @request, @created);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@request", requestJson);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
        return id;
    }

    public async Task<bool> DeploymentRunExistsAsync(string deploymentId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM deployment_runs WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", deploymentId);
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    public async Task CreateRegistryOperationAsync(string operationId, string kind, string? payloadJson)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO registry_ops (id, kind, payload_json, created_at)
            VALUES (@id, @kind, @payload, @created);";
        cmd.Parameters.AddWithValue("@id", operationId);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@payload", (object?)payloadJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task CompleteRegistryOperationAsync(string operationId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE registry_ops SET completed_at = @completed WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", operationId);
        cmd.Parameters.AddWithValue("@completed", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<byte[]> GetOrCreateSecretKeyAsync()
    {
        const string secretName = "service.secret-key";
        var existing = await GetServiceSecretAsync(secretName);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return Convert.FromBase64String(existing);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(key);
        await SetServiceSecretAsync(secretName, encoded);
        return key;
    }

    public async Task<string?> GetServiceSecretAsync(string name)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value FROM service_secrets WHERE name = @name LIMIT 1;";
        cmd.Parameters.AddWithValue("@name", name);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public async Task SetServiceSecretAsync(string name, string value)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT OR REPLACE INTO service_secrets (name, value, updated_at)
            VALUES (@name, @value, @updated);";
        cmd.Parameters.AddWithValue("@name", name);
        cmd.Parameters.AddWithValue("@value", value);
        cmd.Parameters.AddWithValue("@updated", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveUploadAsync(string path, string? contentType, byte[] content)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO uploads (id, path, content_type, content, created_at)
            VALUES (@id, @path, @type, @content, @created);";
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@type", (object?)contentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@content", content);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }
}
