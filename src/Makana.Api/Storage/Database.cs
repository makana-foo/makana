using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Makana.Api.Contracts;

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

        // Cleanup migration for previous personal-namespace modeling that incorrectly
        // created synthetic org rows. Personal namespaces should map to users, not orgs.
        await MigrateLegacyPersonalNamespaceRowsAsync(connection);
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
}

public sealed class BackendStore
{
    private readonly Database _database;

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
            ORDER BY created_at DESC
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        return (string?)await cmd.ExecuteScalarAsync();
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

    public async Task RenameStackAsync(string org, string project, string stack, string newName, string? newProject)
    {
        var targetProject = string.IsNullOrWhiteSpace(newProject) ? project : newProject!;
        var projectId = await EnsureProjectAsync(org, targetProject);

        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
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
        cmd.Parameters.AddWithValue("@newName", newName);
        cmd.Parameters.AddWithValue("@projectId", projectId);
        cmd.Parameters.AddWithValue("@org", org);
        cmd.Parameters.AddWithValue("@project", project);
        cmd.Parameters.AddWithValue("@stack", stack);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DeleteStackAsync(string org, string project, string stack)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
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
        var updateId = Guid.NewGuid().ToString("N");
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO updates (id, stack_id, kind, status, version, program_request_json, created_at)
            VALUES (@id, @stackId, @kind, @status, @version, @program, @created);";
        cmd.Parameters.AddWithValue("@id", updateId);
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@status", "not-started");
        cmd.Parameters.AddWithValue("@version", 0);
        cmd.Parameters.AddWithValue("@program", programRequestJson);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
        return updateId;
    }

    public async Task<int> GetNextStackVersionAsync(string stackId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(version), 0) FROM deployments WHERE stack_id = @stackId;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        var current = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        return current + 1;
    }

    public async Task<string> StartUpdateAsync(string updateId, int version, string startRequestJson)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            UPDATE updates
            SET status = @status, version = @version, start_request_json = @start
            WHERE id = @id;";
        cmd.Parameters.AddWithValue("@status", "in-progress");
        cmd.Parameters.AddWithValue("@version", version);
        cmd.Parameters.AddWithValue("@start", startRequestJson);
        cmd.Parameters.AddWithValue("@id", updateId);
        await cmd.ExecuteNonQueryAsync();

        var token = Guid.NewGuid().ToString("N");
        cmd.CommandText = "INSERT INTO update_tokens (token, update_id, expires_at) VALUES (@token, @updateId, NULL);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@updateId", updateId);
        await cmd.ExecuteNonQueryAsync();
        return token;
    }

    public async Task<bool> HasActiveUpdateAsync(string stackId, string updateId)
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
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<bool> IsUpdateInProgressAsync(string updateId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM updates WHERE id = @id AND status = 'in-progress' LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    public async Task<bool> ValidateUpdateTokenAsync(string updateId, string token)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM update_tokens WHERE token = @token AND update_id = @updateId LIMIT 1;";
        cmd.Parameters.AddWithValue("@token", token);
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var result = await cmd.ExecuteScalarAsync();
        return result != null;
    }

    public async Task SaveCheckpointAsync(string updateId, string deploymentJson, int version)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT stack_id, version FROM updates WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return;
        }

        var stackId = reader.GetString(0);
        var updateVersion = reader.GetInt32(1);
        reader.Close();
        var deploymentVersion = updateVersion > 0 ? updateVersion : version;
        if (deploymentVersion <= 0)
        {
            return;
        }

        cmd.CommandText = "SELECT COALESCE(MAX(sequence), 0) FROM update_checkpoints WHERE update_id = @updateId;";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var sequence = Convert.ToInt32(await cmd.ExecuteScalarAsync()) + 1;

        cmd.CommandText = @"
            INSERT INTO update_checkpoints (id, update_id, sequence, version, deployment_json, created_at)
            VALUES (@id, @updateId, @sequence, @version, @deployment, @created);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@updateId", updateId);
        cmd.Parameters.AddWithValue("@sequence", sequence);
        cmd.Parameters.AddWithValue("@version", deploymentVersion);
        cmd.Parameters.AddWithValue("@deployment", deploymentJson);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();

        cmd.CommandText = @"
            INSERT OR REPLACE INTO deployments (id, stack_id, version, deployment_json, created_at)
            VALUES (@depId, @stackId, @version, @deployment, @created);";
        cmd.Parameters.Clear();
        cmd.Parameters.AddWithValue("@depId", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@version", deploymentVersion);
        cmd.Parameters.AddWithValue("@deployment", deploymentJson);
        cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(int Sequence, int Version, string DeploymentJson)?> GetLatestCheckpointForUpdateAsync(string updateId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT sequence, version, deployment_json
            FROM update_checkpoints
            WHERE update_id = @updateId
            ORDER BY sequence DESC
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2)
            );
        }
        return null;
    }

    public async Task RecordEventsAsync(string updateId, IEnumerable<string> eventsJson)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(sequence), 0) FROM update_events WHERE update_id = @updateId;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        var sequence = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        foreach (var json in eventsJson)
        {
            sequence += 1;
            cmd.CommandText = @"
                INSERT INTO update_events (id, update_id, sequence, event_json, created_at)
                VALUES (@id, @updateId, @sequence, @event, @created);";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@updateId", updateId);
            cmd.Parameters.AddWithValue("@sequence", sequence);
            cmd.Parameters.AddWithValue("@event", json);
            cmd.Parameters.AddWithValue("@created", DateTimeOffset.UtcNow.ToString("O"));
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task RecordJournalEntriesAsync(string updateId, IEnumerable<string> entriesJson)
    {
        await RecordEventsAsync(updateId, entriesJson);
    }

    public async Task<List<string>> GetEventsAsync(string updateId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT event_json FROM update_events WHERE update_id = @updateId ORDER BY sequence ASC;";
        cmd.Parameters.AddWithValue("@updateId", updateId);
        using var reader = await cmd.ExecuteReaderAsync();
        var results = new List<string>();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }
        return results;
    }

    public async Task CompleteUpdateAsync(string updateId, string status)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE updates SET status = @status, completed_at = @completed WHERE id = @id;";
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@completed", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", updateId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task CancelUpdateAsync(string updateId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE updates SET status = @status, completed_at = @completed WHERE id = @id;";
        cmd.Parameters.AddWithValue("@status", "canceled");
        cmd.Parameters.AddWithValue("@completed", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("@id", updateId);
        await cmd.ExecuteNonQueryAsync();
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

    public async Task<(string StackId, string Kind, int Version)?> GetUpdateContextAsync(string updateId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT stack_id, kind, version FROM updates WHERE id = @id LIMIT 1;";
        cmd.Parameters.AddWithValue("@id", updateId);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2)
            );
        }
        return null;
    }

    public async Task<List<(string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson)>>
        GetHistoryAsync(string stackId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT kind, status, version, created_at, completed_at, program_request_json
            FROM updates
            WHERE stack_id = @stackId
            ORDER BY created_at DESC;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        using var reader = await cmd.ExecuteReaderAsync();
        var list = new List<(string, string, int, string, string?, string?)>();
        while (await reader.ReadAsync())
        {
            list.Add((
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)
            ));
        }
        return list;
    }

    public async Task<(string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson)?>
        GetUpdateByVersionAsync(string stackId, int version)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT kind, status, version, created_at, completed_at, program_request_json
            FROM updates
            WHERE stack_id = @stackId AND version = @version
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        cmd.Parameters.AddWithValue("@version", version);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)
            );
        }
        return null;
    }

    public async Task<(int Version, string DeploymentJson)?> GetLatestDeploymentAsync(string stackId)
    {
        using var connection = _database.OpenConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            SELECT version, deployment_json
            FROM deployments
            WHERE stack_id = @stackId
            ORDER BY version DESC
            LIMIT 1;";
        cmd.Parameters.AddWithValue("@stackId", stackId);
        using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return (reader.GetInt32(0), reader.GetString(1));
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
