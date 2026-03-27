using System.Security.Cryptography;
using Dapper;

namespace Makana.Api.Storage;

public sealed record ApiTokenRecord(
    string Token,
    string TokenId,
    string TokenType,
    string PrincipalType,
    string PrincipalId,
    string UserId,
    string? OrgId,
    string? TeamId,
    string? DisplayName,
    bool ScopeAdmin);

public sealed record LinkedIdentityResolution(
    string IdentityId,
    string UserId,
    string Login);

public sealed class IdentityRepository(SqliteControlDatabase database)
{
    public void EnsureApiToken(string token, string userId, string? orgId, DateTimeOffset? expiresAt)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT INTO api_tokens (
                token,
                token_id,
                token_type,
                principal_type,
                principal_id,
                user_id,
                org_id,
                issued_at,
                expires_at,
                issued_via,
                scope_admin)
            VALUES (
                @token,
                @tokenId,
                @tokenType,
                @principalType,
                @principalId,
                @user,
                @org,
                @issuedAt,
                @expires,
                @issuedVia,
                0)
            ON CONFLICT(token) DO UPDATE SET
                token_id = excluded.token_id,
                token_type = excluded.token_type,
                principal_type = excluded.principal_type,
                principal_id = excluded.principal_id,
                user_id = excluded.user_id,
                org_id = excluded.org_id,
                issued_at = excluded.issued_at,
                issued_via = excluded.issued_via,
                expires_at = excluded.expires_at;
            """,
            new
            {
                token,
                tokenId = Guid.NewGuid().ToString("N"),
                tokenType = orgId is null ? "personal" : "organization",
                principalType = orgId is null ? "user" : "organization",
                principalId = orgId ?? userId,
                user = userId,
                org = orgId,
                issuedAt = DateTimeOffset.UtcNow.ToString("O"),
                issuedVia = "legacy",
                expires = expiresAt?.ToString("O")
            });
    }

    public void CreateAccessToken(
        string token,
        string tokenType,
        string principalType,
        string principalId,
        string userId,
        string? orgId,
        string? teamId,
        string? displayName,
        string? description,
        DateTimeOffset issuedAt,
        DateTimeOffset? expiresAt,
        string issuedVia,
        string? linkedIdentityId,
        string? createdByUserId,
        bool scopeAdmin)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT INTO api_tokens (
                token,
                token_id,
                token_type,
                principal_type,
                principal_id,
                user_id,
                org_id,
                team_id,
                display_name,
                description,
                issued_at,
                expires_at,
                issued_via,
                linked_identity_id,
                created_by_user_id,
                scope_admin)
            VALUES (
                @token,
                @tokenId,
                @tokenType,
                @principalType,
                @principalId,
                @userId,
                @orgId,
                @teamId,
                @displayName,
                @description,
                @issuedAt,
                @expiresAt,
                @issuedVia,
                @linkedIdentityId,
                @createdByUserId,
                @scopeAdmin);
            """,
            new
            {
                token,
                tokenId = Guid.NewGuid().ToString("N"),
                tokenType,
                principalType,
                principalId,
                userId,
                orgId,
                teamId,
                displayName,
                description,
                issuedAt = issuedAt.ToString("O"),
                expiresAt = expiresAt?.ToString("O"),
                issuedVia,
                linkedIdentityId,
                createdByUserId,
                scopeAdmin = scopeAdmin ? 1 : 0
            });
    }

    public (string UserId, string? OrgId)? GetApiTokenOwner(string token)
    {
        var record = GetApiToken(token);
        return record is null ? null : (record.UserId, record.OrgId);
    }

    public ApiTokenRecord? GetApiToken(string token)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<ApiTokenRecord>(
            """
            SELECT
                token AS Token,
                COALESCE(token_id, token) AS TokenId,
                COALESCE(token_type, CASE
                    WHEN team_id IS NOT NULL THEN 'team'
                    WHEN org_id IS NOT NULL THEN 'organization'
                    ELSE 'personal'
                END) AS TokenType,
                COALESCE(principal_type, CASE
                    WHEN team_id IS NOT NULL THEN 'team'
                    WHEN org_id IS NOT NULL THEN 'organization'
                    ELSE 'user'
                END) AS PrincipalType,
                COALESCE(principal_id, CASE
                    WHEN team_id IS NOT NULL THEN team_id
                    WHEN org_id IS NOT NULL THEN org_id
                    ELSE user_id
                END) AS PrincipalId,
                user_id AS UserId,
                org_id AS OrgId,
                team_id AS TeamId,
                display_name AS DisplayName,
                CAST(COALESCE(scope_admin, 0) AS INTEGER) AS ScopeAdmin
            FROM api_tokens
            WHERE token = @token
              AND (expires_at IS NULL OR expires_at > @now)
              AND revoked_at IS NULL
            LIMIT 1;
            """,
            new
            {
                token,
                now = DateTimeOffset.UtcNow.ToString("O")
            });
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

    public string EnsureTeam(string orgId, string teamName, string? displayName = null)
    {
        using var connection = database.OpenConnection();
        var existingTeamId = connection.QuerySingleOrDefault<string>(
            """
            SELECT id
            FROM teams
            WHERE org_id = @orgId
              AND name = @teamName COLLATE NOCASE
            LIMIT 1;
            """,
            new { orgId, teamName });
        if (!string.IsNullOrWhiteSpace(existingTeamId))
        {
            connection.Execute(
                """
                UPDATE teams
                SET display_name = COALESCE(@displayName, display_name)
                WHERE id = @teamId;
                """,
                new { teamId = existingTeamId, displayName });
            return existingTeamId!;
        }

        var teamId = "team-" + Guid.NewGuid().ToString("N");
        connection.Execute(
            """
            INSERT INTO teams (id, org_id, name, display_name)
            VALUES (@id, @orgId, @teamName, @displayName);
            """,
            new
            {
                id = teamId,
                orgId,
                teamName,
                displayName = displayName ?? teamName
            });
        return teamId;
    }

    public void EnsureTeamMembership(string teamId, string userId)
    {
        using var connection = database.OpenConnection();
        connection.Execute(
            """
            INSERT OR IGNORE INTO team_memberships (team_id, user_id)
            VALUES (@teamId, @userId);
            """,
            new { teamId, userId });
    }

    public (string Id, string Name)? GetTeamByOrgAndName(string orgId, string teamName)
    {
        using var connection = database.OpenConnection();
        var row = connection.QuerySingleOrDefault<TeamRow>(
            """
            SELECT id, name
            FROM teams
            WHERE org_id = @orgId
              AND name = @teamName COLLATE NOCASE
            LIMIT 1;
            """,
            new { orgId, teamName });
        return row is null ? null : (row.Id, row.Name);
    }

    public string? GetTeamNameById(string teamId)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<string>(
            """
            SELECT name
            FROM teams
            WHERE id = @teamId
            LIMIT 1;
            """,
            new { teamId });
    }

    public bool IsUserMemberOfTeam(string userId, string teamId)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<int?>(
            """
            SELECT 1
            FROM team_memberships
            WHERE team_id = @teamId
              AND user_id = @userId
            LIMIT 1;
            """,
            new { teamId, userId }) is not null;
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

    public LinkedIdentityResolution ResolveLinkedIdentity(
        string issuer,
        string subject,
        string login,
        string displayName,
        string? email,
        bool emailVerified)
    {
        var normalizedLogin = NormalizeLogin(login, subject);
        var effectiveDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? normalizedLogin
            : displayName.Trim();
        var now = DateTimeOffset.UtcNow.ToString("O");

        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();

        var linkedIdentity = connection.QuerySingleOrDefault<LinkedIdentityRow>(
            """
            SELECT id, user_id
            FROM linked_identities
            WHERE issuer = @issuer
              AND subject = @subject
            LIMIT 1;
            """,
            new { issuer, subject },
            transaction);

        var userId = linkedIdentity?.UserId
            ?? connection.QuerySingleOrDefault<string>(
                """
                SELECT id
                FROM users
                WHERE github_login = @login COLLATE NOCASE
                LIMIT 1;
                """,
                new { login = normalizedLogin },
                transaction)
            ?? "user-" + Guid.NewGuid().ToString("N");

        connection.Execute(
            """
            INSERT INTO users (id, github_login, display_name, email, avatar_url)
            VALUES (@id, @login, @displayName, @email, @avatarUrl)
            ON CONFLICT(id) DO UPDATE SET
                github_login = excluded.github_login,
                display_name = excluded.display_name,
                email = excluded.email,
                avatar_url = excluded.avatar_url;
            """,
            new
            {
                id = userId,
                login = normalizedLogin,
                displayName = effectiveDisplayName,
                email,
                avatarUrl = string.Empty
            },
            transaction);

        var identityId = linkedIdentity?.Id ?? "identity-" + Guid.NewGuid().ToString("N");
        connection.Execute(
            """
            INSERT INTO linked_identities (
                id,
                user_id,
                issuer,
                subject,
                preferred_username,
                email,
                email_verified,
                last_seen_at)
            VALUES (
                @id,
                @userId,
                @issuer,
                @subject,
                @preferredUsername,
                @email,
                @emailVerified,
                @lastSeenAt)
            ON CONFLICT(issuer, subject) DO UPDATE SET
                user_id = excluded.user_id,
                preferred_username = excluded.preferred_username,
                email = excluded.email,
                email_verified = excluded.email_verified,
                last_seen_at = excluded.last_seen_at;
            """,
            new
            {
                id = identityId,
                userId,
                issuer,
                subject,
                preferredUsername = normalizedLogin,
                email,
                emailVerified = emailVerified ? 1 : 0,
                lastSeenAt = now
            },
            transaction);

        transaction.Commit();
        return new LinkedIdentityResolution(identityId, userId, normalizedLogin);
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

    private static string NormalizeLogin(string login, string subject)
    {
        if (!string.IsNullOrWhiteSpace(login))
        {
            return login.Trim();
        }

        return $"user-{subject.Trim()}";
    }

    internal sealed class ApiTokenOwnerRow
    {
        public required string UserId { get; init; }
        public string? OrgId { get; init; }
    }

    internal sealed class UserRow
    {
        public required string Id { get; init; }
        public required string Login { get; init; }
        public required string Name { get; init; }
        public string? Email { get; init; }
        public string? AvatarUrl { get; init; }
    }

    internal sealed class UserOrgRow
    {
        public required string Id { get; init; }
        public required string Login { get; init; }
    }

    internal sealed class TeamRow
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
    }

    internal sealed class LinkedIdentityRow
    {
        public required string Id { get; init; }
        public required string UserId { get; init; }
    }
}
