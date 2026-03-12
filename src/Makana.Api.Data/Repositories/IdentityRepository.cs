using System.Security.Cryptography;
using Dapper;

namespace Makana.Api.Storage;

public sealed class IdentityRepository(SqliteControlDatabase database)
{
    public void EnsureApiToken(string token, string userId, string? orgId, DateTimeOffset? expiresAt)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT INTO api_tokens (token, user_id, org_id, expires_at)
            VALUES (@token, @user, @org, @expires)
            ON CONFLICT(token) DO UPDATE SET
                user_id = excluded.user_id,
                org_id = excluded.org_id,
                expires_at = excluded.expires_at;
            """,
            new
            {
                token,
                user = userId,
                org = orgId,
                expires = expiresAt?.ToString("O")
            });
    }

    public (string UserId, string? OrgId)? GetApiTokenOwner(string token)
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<ApiTokenOwnerRow>(
            """
            SELECT user_id, org_id
            FROM api_tokens
            WHERE token = @token
              AND (expires_at IS NULL OR expires_at > @now)
            LIMIT 1;
            """,
            new
            {
                token,
                now = DateTimeOffset.UtcNow.ToString("O")
            });
        return row is null ? null : (row.UserId, row.OrgId);
    }

    public (string Id, string Login, string Name, string? Email, string AvatarUrl)? GetUserById(string userId)
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<UserRow>(
            """
            SELECT
                id AS Id,
                github_login AS Login,
                display_name AS Name,
                email AS Email,
                avatar_url AS AvatarUrl
            FROM users
            WHERE id = @userId
            LIMIT 1;
            """,
            new { userId });
        return row is null
            ? null
            : (row.Id, row.Login, row.Name, row.Email, row.AvatarUrl ?? string.Empty);
    }

    public string? GetUserIdByLogin(string login)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<string>(
            "SELECT id FROM users WHERE github_login = @login LIMIT 1;",
            new { login });
    }

    public string? GetUserTheme(string userId)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<string>(
            """
            SELECT theme
            FROM user_preferences
            WHERE user_id = @userId
            LIMIT 1;
            """,
            new { userId });
    }

    public void SetUserTheme(string userId, string theme)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT INTO user_preferences (user_id, theme, updated_at)
            VALUES (@userId, @theme, @updatedAt)
            ON CONFLICT(user_id) DO UPDATE SET
                theme = excluded.theme,
                updated_at = excluded.updated_at;
            """,
            new
            {
                userId,
                theme,
                updatedAt = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    public List<(string Id, string Login)> GetUserOrgs(string userId)
    {
        using var connection = database.OpenConnection();
        var rows = connection.Query<UserOrgRow>(
            """
            SELECT o.id, o.login
            FROM org_memberships m
            JOIN orgs o ON o.id = m.org_id
            WHERE m.user_id = @userId
            ORDER BY o.login;
            """,
            new { userId });
        return [.. rows.Select(row => (row.Id, row.Login))];
    }

    public bool IsUserMemberOfOrgLogin(string userId, string orgLogin)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<int?>(
            """
            SELECT 1
            FROM org_memberships m
            JOIN orgs o ON o.id = m.org_id
            WHERE m.user_id = @userId
              AND o.login = @orgLogin
            LIMIT 1;
            """,
            new { userId, orgLogin }) is not null;
    }

    public void EnsureUser(
        string userId,
        string login,
        string displayName,
        string? email,
        string avatarUrl)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT INTO users (id, github_login, display_name, email, avatar_url)
            VALUES (@id, @login, @displayName, @email, @avatar)
            ON CONFLICT(id) DO UPDATE SET
                github_login = excluded.github_login,
                display_name = excluded.display_name,
                email = excluded.email,
                avatar_url = excluded.avatar_url;
            """,
            new
            {
                id = userId,
                login,
                displayName,
                email,
                avatar = avatarUrl
            });
    }

    public string? GetOrgIdByLogin(string login)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<string>(
            "SELECT id FROM orgs WHERE login = @login LIMIT 1;",
            new { login });
    }

    public string? GetOrgLoginById(string orgId)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<string>(
            "SELECT login FROM orgs WHERE id = @orgId LIMIT 1;",
            new { orgId });
    }

    public byte[] GetOrCreateSecretKey()
    {
        const string secretName = "service.secret-key";
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        var existing = connection.QuerySingleOrDefault<string>(
            "SELECT value FROM service_secrets WHERE name = @name LIMIT 1;",
            new { name = secretName },
            transaction);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            transaction.Commit();
            return Convert.FromBase64String(existing);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var encoded = Convert.ToBase64String(key);
        connection.Execute(
            """
            INSERT OR REPLACE INTO service_secrets (name, value, updated_at)
            VALUES (@name, @value, @updated);
            """,
            new
            {
                name = secretName,
                value = encoded,
                updated = DateTimeOffset.UtcNow.ToString("O")
            },
            transaction);
        transaction.Commit();
        return key;
    }
}
