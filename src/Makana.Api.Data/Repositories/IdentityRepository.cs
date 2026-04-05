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

public sealed record OrganizationRecord(
    string Id,
    string Login,
    string DisplayName);

public sealed record OrganizationMemberRecord(
    string UserId,
    string Login,
    string DisplayName,
    string? Email,
    string Role,
    string MembershipKind);

public sealed record OrganizationInviteRecord(
    string Id,
    string Email,
    string Role,
    DateTimeOffset CreatedAt,
    bool Claimed,
    bool Revoked);

public sealed record OrganizationTokenRecord(
    string TokenId,
    string DisplayName,
    string? Description,
    string? CreatedByUserId,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt,
    bool Revoked);

public sealed record OrganizationSsoConfigurationRecord(
    string Protocol,
    string Mode,
    string? ConnectionId,
    string? DisplayName,
    string? ScimBearerToken,
    IReadOnlyList<string> Domains,
    OrganizationOidcConfigurationRecord? Oidc,
    OrganizationSamlConfigurationRecord? Saml);

public sealed record OrganizationOidcConfigurationRecord(
    string Issuer,
    string ClientId,
    string ClientSecret);

public sealed record OrganizationSamlConfigurationRecord(
    string SsoUrl,
    string CaCertificatePem,
    string UsernameAttribute,
    string EmailAttribute,
    string? GroupsAttribute,
    string? EntityIssuer,
    string? SsoIssuer,
    string? GroupsDelimiter,
    string? NameIdPolicyFormat);

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

    public OrganizationRecord CreateOrganization(string displayName, string login, string creatorUserId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();

        var existing = connection.QuerySingleOrDefault<OrganizationRecord>(
            """
            SELECT id AS Id, login AS Login, COALESCE(display_name, login) AS DisplayName
            FROM orgs
            WHERE login = @login
            LIMIT 1;
            """,
            new { login },
            transaction);
        if (existing is not null)
        {
            throw new InvalidOperationException("organization already exists");
        }

        var orgId = "org-" + Guid.NewGuid().ToString("N");
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? login
            : displayName.Trim();

        connection.Execute(
            """
            INSERT INTO orgs (id, login, display_name)
            VALUES (@id, @login, @displayName);
            """,
            new { id = orgId, login, displayName = normalizedDisplayName },
            transaction);

        connection.Execute(
            """
            INSERT INTO org_memberships (org_id, user_id, role)
            VALUES (@orgId, @userId, 'admin');
            """,
            new { orgId, userId = creatorUserId },
            transaction);

        transaction.Commit();
        return new OrganizationRecord(orgId, login, normalizedDisplayName);
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

    public bool IsUserAdminOfOrgLogin(string userId, string orgLogin)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<int?>(
            """
            SELECT 1
            FROM org_memberships m
            JOIN orgs o ON o.id = m.org_id
            WHERE m.user_id = @userId
              AND o.login = @orgLogin
              AND m.role = 'admin'
            LIMIT 1;
            """,
            new { userId, orgLogin }) is not null;
    }

    public OrganizationRecord? GetOrganizationByLogin(string login)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<OrganizationRecord>(
            """
            SELECT id AS Id, login AS Login, COALESCE(display_name, login) AS DisplayName
            FROM orgs
            WHERE login = @login
            LIMIT 1;
            """,
            new { login });
    }

    public List<OrganizationMemberRecord> ListOrganizationMembers(string orgLogin)
    {
        using var connection = database.OpenConnection();
        var activeMembers = connection.Query<OrganizationMemberRecord>(
            """
            SELECT
                u.id AS UserId,
                u.github_login AS Login,
                u.display_name AS DisplayName,
                u.email AS Email,
                m.role AS Role,
                'active' AS MembershipKind
            FROM org_memberships m
            JOIN orgs o ON o.id = m.org_id
            JOIN users u ON u.id = m.user_id
            WHERE o.login = @orgLogin
            ORDER BY u.github_login;
            """,
            new { orgLogin });

        var provisionedMembers = connection.Query<OrganizationMemberRecord>(
            """
            SELECT
                COALESCE(s.claimed_user_id, s.id) AS UserId,
                LOWER(s.email) AS Login,
                COALESCE(s.display_name, s.email) AS DisplayName,
                s.email AS Email,
                s.role AS Role,
                'provisioned' AS MembershipKind
            FROM org_scim_memberships s
            JOIN orgs o ON o.id = s.org_id
            WHERE o.login = @orgLogin
              AND s.active = 1
              AND s.claimed_user_id IS NULL
            ORDER BY email;
            """,
            new { orgLogin });

        return [.. activeMembers, .. provisionedMembers];
    }

    public string CreateOrganizationInvite(string orgLogin, string email, string role)
    {
        var normalizedEmail = NormalizeEmail(email);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();

        var orgId = connection.QuerySingleOrDefault<string>(
            "SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1;",
            new { orgLogin },
            transaction) ?? throw new InvalidOperationException("organization not found");

        var inviteId = "invite-" + Guid.NewGuid().ToString("N");
        connection.Execute(
            """
            INSERT INTO org_invitations (id, org_id, email, role, created_at)
            VALUES (@id, @orgId, @email, @role, @createdAt);
            """,
            new
            {
                id = inviteId,
                orgId,
                email = normalizedEmail,
                role = NormalizeOrgRole(role),
                createdAt = DateTimeOffset.UtcNow.ToString("O")
            },
            transaction);
        transaction.Commit();
        return inviteId;
    }

    public List<OrganizationInviteRecord> ListOrganizationInvites(string orgLogin)
    {
        using var connection = database.OpenConnection();
        return
        [
            .. connection.Query<OrganizationInviteRecord>(
                """
                SELECT
                    i.id AS Id,
                    i.email AS Email,
                    i.role AS Role,
                    i.created_at AS CreatedAt,
                    CAST(i.claimed_at IS NOT NULL AS INTEGER) AS Claimed,
                    CAST(i.revoked_at IS NOT NULL AS INTEGER) AS Revoked
                FROM org_invitations i
                JOIN orgs o ON o.id = i.org_id
                WHERE o.login = @orgLogin
                ORDER BY i.created_at DESC;
                """,
                new { orgLogin })
        ];
    }

    public bool RevokeOrganizationInvite(string orgLogin, string inviteId)
    {
        using var connection = database.OpenConnection();
        return connection.Execute(
            """
            UPDATE org_invitations
            SET revoked_at = @revokedAt
            WHERE id = @inviteId
              AND org_id = (SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1)
              AND revoked_at IS NULL
              AND claimed_at IS NULL;
            """,
            new
            {
                inviteId,
                orgLogin,
                revokedAt = DateTimeOffset.UtcNow.ToString("O")
            }) > 0;
    }

    public int ClaimOrganizationMembershipsForVerifiedEmail(string userId, string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");

        var inviteRows = connection.Query<ClaimableMembershipRow>(
            """
            SELECT org_id AS OrgId, role AS Role
            FROM org_invitations
            WHERE email = @email
              AND claimed_at IS NULL
              AND revoked_at IS NULL;
            """,
            new { email = normalizedEmail },
            transaction);

        foreach (var invite in inviteRows)
        {
            connection.Execute(
                """
                INSERT INTO org_memberships (org_id, user_id, role)
                VALUES (@orgId, @userId, @role)
                ON CONFLICT(org_id, user_id) DO UPDATE SET
                    role = CASE
                        WHEN excluded.role = 'admin' THEN 'admin'
                        ELSE org_memberships.role
                    END;
                """,
                new { orgId = invite.OrgId, userId, role = NormalizeOrgRole(invite.Role) },
                transaction);
        }

        connection.Execute(
            """
            UPDATE org_invitations
            SET claimed_at = @now
            WHERE email = @email
              AND claimed_at IS NULL
              AND revoked_at IS NULL;
            """,
            new { email = normalizedEmail, now },
            transaction);

        var scimRows = connection.Query<ClaimableMembershipRow>(
            """
            SELECT org_id AS OrgId, role AS Role
            FROM org_scim_memberships
            WHERE email = @email
              AND active = 1
              AND claimed_user_id IS NULL;
            """,
            new { email = normalizedEmail },
            transaction);

        foreach (var provisioned in scimRows)
        {
            connection.Execute(
                """
                INSERT INTO org_memberships (org_id, user_id, role)
                VALUES (@orgId, @userId, @role)
                ON CONFLICT(org_id, user_id) DO UPDATE SET
                    role = CASE
                        WHEN excluded.role = 'admin' THEN 'admin'
                        ELSE org_memberships.role
                    END;
                """,
                new { orgId = provisioned.OrgId, userId, role = NormalizeOrgRole(provisioned.Role) },
                transaction);
        }

        connection.Execute(
            """
            UPDATE org_scim_memberships
            SET claimed_user_id = @userId,
                updated_at = @now
            WHERE email = @email
              AND active = 1
              AND claimed_user_id IS NULL;
            """,
            new { email = normalizedEmail, userId, now },
            transaction);

        transaction.Commit();
        return inviteRows.Count() + scimRows.Count();
    }

    public bool SetOrganizationMemberRole(string orgLogin, string userId, string role)
    {
        using var connection = database.OpenConnection();
        return connection.Execute(
            """
            UPDATE org_memberships
            SET role = @role
            WHERE user_id = @userId
              AND org_id = (SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1);
            """,
            new { orgLogin, userId, role = NormalizeOrgRole(role) }) > 0;
    }

    public bool RemoveOrganizationMember(string orgLogin, string userId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();

        var orgId = connection.QuerySingleOrDefault<string>(
            "SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1;",
            new { orgLogin },
            transaction);
        if (string.IsNullOrWhiteSpace(orgId))
        {
            transaction.Commit();
            return false;
        }

        var removed = connection.Execute(
            """
            DELETE FROM org_memberships
            WHERE org_id = @orgId
              AND user_id = @userId;
            """,
            new { orgId, userId },
            transaction);

        connection.Execute(
            """
            UPDATE api_tokens
            SET revoked_at = @revokedAt
            WHERE org_id = @orgId
              AND user_id = @userId
              AND revoked_at IS NULL;
            """,
            new { orgId, userId, revokedAt = DateTimeOffset.UtcNow.ToString("O") },
            transaction);

        transaction.Commit();
        return removed > 0;
    }

    public List<OrganizationTokenRecord> ListOrganizationTokens(string orgLogin)
    {
        using var connection = database.OpenConnection();
        return
        [
            .. connection.Query<OrganizationTokenRecord>(
                """
                SELECT
                    token_id AS TokenId,
                    COALESCE(display_name, token_type) AS DisplayName,
                    description AS Description,
                    created_by_user_id AS CreatedByUserId,
                    issued_at AS IssuedAt,
                    expires_at AS ExpiresAt,
                    CAST(revoked_at IS NOT NULL AS INTEGER) AS Revoked
                FROM api_tokens
                WHERE org_id = (SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1)
                ORDER BY issued_at DESC;
                """,
                new { orgLogin })
        ];
    }

    public bool RevokeOrganizationToken(string orgLogin, string tokenId)
    {
        using var connection = database.OpenConnection();
        return connection.Execute(
            """
            UPDATE api_tokens
            SET revoked_at = @revokedAt
            WHERE token_id = @tokenId
              AND org_id = (SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1)
              AND revoked_at IS NULL;
            """,
            new { tokenId, orgLogin, revokedAt = DateTimeOffset.UtcNow.ToString("O") }) > 0;
    }

    public OrganizationSsoConfigurationRecord? GetOrganizationSsoConfiguration(string orgLogin)
    {
        using var connection = database.OpenConnection();
        var config = connection.QuerySingleOrDefault<OrganizationSsoConfigRow>(
            """
            SELECT
                c.protocol AS Protocol,
                c.mode AS Mode,
                c.connector_id AS ConnectionId,
                c.display_name AS DisplayName,
                c.scim_bearer_token AS ScimBearerToken,
                c.oidc_issuer AS OidcIssuer,
                c.oidc_client_id AS OidcClientId,
                c.oidc_client_secret AS OidcClientSecret,
                c.saml_sso_url AS SamlSsoUrl,
                c.saml_ca_certificate_pem AS SamlCaCertificatePem,
                c.saml_username_attribute AS SamlUsernameAttribute,
                c.saml_email_attribute AS SamlEmailAttribute,
                c.saml_groups_attribute AS SamlGroupsAttribute,
                c.saml_entity_issuer AS SamlEntityIssuer,
                c.saml_sso_issuer AS SamlSsoIssuer,
                c.saml_groups_delimiter AS SamlGroupsDelimiter,
                c.saml_name_id_policy_format AS SamlNameIdPolicyFormat
            FROM org_sso_configs c
            JOIN orgs o ON o.id = c.org_id
            WHERE o.login = @orgLogin
            LIMIT 1;
            """,
            new { orgLogin });
        if (config is null)
        {
            return null;
        }

        var domains = connection.Query<string>(
            """
            SELECT domain
            FROM org_verified_domains
            WHERE org_id = (SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1)
            ORDER BY domain;
            """,
            new { orgLogin }).ToArray();

        return new OrganizationSsoConfigurationRecord(
            config.Protocol,
            config.Mode,
            config.ConnectionId,
            config.DisplayName,
            config.ScimBearerToken,
            domains,
            BuildOidcConfiguration(config),
            BuildSamlConfiguration(config));
    }

    public OrganizationRecord? GetOrganizationByVerifiedEmailDomain(string domain)
    {
        using var connection = database.OpenConnection();
        return connection.QuerySingleOrDefault<OrganizationRecord>(
            """
            SELECT o.id AS Id, o.login AS Login, COALESCE(o.display_name, o.login) AS DisplayName
            FROM org_verified_domains d
            JOIN orgs o ON o.id = d.org_id
            WHERE d.domain = @domain
            LIMIT 1;
            """,
            new { domain = NormalizeDomain(domain) });
    }

    public void UpsertOrganizationSsoConfiguration(
        string orgLogin,
        string protocol,
        string mode,
        string? connectionId,
        string? displayName,
        string? scimBearerToken,
        IEnumerable<string> domains,
        OrganizationOidcConfigurationRecord? oidc,
        OrganizationSamlConfigurationRecord? saml)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        var orgId = connection.QuerySingleOrDefault<string>(
            "SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1;",
            new { orgLogin },
            transaction) ?? throw new InvalidOperationException("organization not found");

        var normalizedProtocol = NormalizeSsoProtocol(protocol);
        var normalizedMode = NormalizeSsoMode(mode);
        connection.Execute(
            """
            INSERT INTO org_sso_configs (
                org_id,
                protocol,
                mode,
                connector_id,
                display_name,
                scim_bearer_token,
                oidc_issuer,
                oidc_client_id,
                oidc_client_secret,
                saml_sso_url,
                saml_ca_certificate_pem,
                saml_username_attribute,
                saml_email_attribute,
                saml_groups_attribute,
                saml_entity_issuer,
                saml_sso_issuer,
                saml_groups_delimiter,
                saml_name_id_policy_format,
                updated_at)
            VALUES (
                @orgId,
                @protocol,
                @mode,
                @connectionId,
                @displayName,
                @scimBearerToken,
                @oidcIssuer,
                @oidcClientId,
                @oidcClientSecret,
                @samlSsoUrl,
                @samlCaCertificatePem,
                @samlUsernameAttribute,
                @samlEmailAttribute,
                @samlGroupsAttribute,
                @samlEntityIssuer,
                @samlSsoIssuer,
                @samlGroupsDelimiter,
                @samlNameIdPolicyFormat,
                @updatedAt)
            ON CONFLICT(org_id) DO UPDATE SET
                protocol = excluded.protocol,
                mode = excluded.mode,
                connector_id = excluded.connector_id,
                display_name = excluded.display_name,
                scim_bearer_token = COALESCE(excluded.scim_bearer_token, org_sso_configs.scim_bearer_token),
                oidc_issuer = excluded.oidc_issuer,
                oidc_client_id = excluded.oidc_client_id,
                oidc_client_secret = excluded.oidc_client_secret,
                saml_sso_url = excluded.saml_sso_url,
                saml_ca_certificate_pem = excluded.saml_ca_certificate_pem,
                saml_username_attribute = excluded.saml_username_attribute,
                saml_email_attribute = excluded.saml_email_attribute,
                saml_groups_attribute = excluded.saml_groups_attribute,
                saml_entity_issuer = excluded.saml_entity_issuer,
                saml_sso_issuer = excluded.saml_sso_issuer,
                saml_groups_delimiter = excluded.saml_groups_delimiter,
                saml_name_id_policy_format = excluded.saml_name_id_policy_format,
                updated_at = excluded.updated_at;
            """,
            new
            {
                orgId,
                protocol = normalizedProtocol,
                mode = normalizedMode,
                connectionId,
                displayName,
                scimBearerToken,
                oidcIssuer = oidc?.Issuer,
                oidcClientId = oidc?.ClientId,
                oidcClientSecret = oidc?.ClientSecret,
                samlSsoUrl = saml?.SsoUrl,
                samlCaCertificatePem = saml?.CaCertificatePem,
                samlUsernameAttribute = saml?.UsernameAttribute,
                samlEmailAttribute = saml?.EmailAttribute,
                samlGroupsAttribute = saml?.GroupsAttribute,
                samlEntityIssuer = saml?.EntityIssuer,
                samlSsoIssuer = saml?.SsoIssuer,
                samlGroupsDelimiter = saml?.GroupsDelimiter,
                samlNameIdPolicyFormat = saml?.NameIdPolicyFormat,
                updatedAt = DateTimeOffset.UtcNow.ToString("O")
            },
            transaction);

        connection.Execute(
            "DELETE FROM org_verified_domains WHERE org_id = @orgId;",
            new { orgId },
            transaction);

        foreach (var domain in domains.Select(NormalizeDomain).Where(static d => !string.IsNullOrWhiteSpace(d)).Distinct(StringComparer.Ordinal))
        {
            connection.Execute(
                """
                INSERT INTO org_verified_domains (org_id, domain)
                VALUES (@orgId, @domain);
                """,
                new { orgId, domain },
                transaction);
        }

        transaction.Commit();
    }

    public bool DeleteOrganizationSsoConfiguration(string orgLogin)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        var orgId = connection.QuerySingleOrDefault<string>(
            "SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1;",
            new { orgLogin },
            transaction);
        if (string.IsNullOrWhiteSpace(orgId))
        {
            transaction.Commit();
            return false;
        }

        connection.Execute("DELETE FROM org_verified_domains WHERE org_id = @orgId;", new { orgId }, transaction);
        var removed = connection.Execute("DELETE FROM org_sso_configs WHERE org_id = @orgId;", new { orgId }, transaction);
        transaction.Commit();
        return removed > 0;
    }

    public OrganizationSsoConfigurationRecord? GetOrganizationSsoConfigurationByScimBearerToken(string bearerToken)
    {
        using var connection = database.OpenConnection();
        var config = connection.QuerySingleOrDefault<OrganizationSsoLookupRow>(
            """
            SELECT
                o.login AS Login,
                c.protocol AS Protocol,
                c.mode AS Mode,
                c.connector_id AS ConnectionId,
                c.display_name AS DisplayName,
                c.scim_bearer_token AS ScimBearerToken,
                c.oidc_issuer AS OidcIssuer,
                c.oidc_client_id AS OidcClientId,
                c.oidc_client_secret AS OidcClientSecret,
                c.saml_sso_url AS SamlSsoUrl,
                c.saml_ca_certificate_pem AS SamlCaCertificatePem,
                c.saml_username_attribute AS SamlUsernameAttribute,
                c.saml_email_attribute AS SamlEmailAttribute,
                c.saml_groups_attribute AS SamlGroupsAttribute,
                c.saml_entity_issuer AS SamlEntityIssuer,
                c.saml_sso_issuer AS SamlSsoIssuer,
                c.saml_groups_delimiter AS SamlGroupsDelimiter,
                c.saml_name_id_policy_format AS SamlNameIdPolicyFormat
            FROM org_sso_configs c
            JOIN orgs o ON o.id = c.org_id
            WHERE c.scim_bearer_token = @bearerToken
            LIMIT 1;
            """,
            new { bearerToken });
        if (config is null)
        {
            return null;
        }

        var domains = connection.Query<string>(
            """
            SELECT d.domain
            FROM org_verified_domains d
            JOIN orgs o ON o.id = d.org_id
            WHERE o.login = @orgLogin
            ORDER BY d.domain;
            """,
            new { orgLogin = config.Login }).ToArray();

        return new OrganizationSsoConfigurationRecord(
            config.Protocol,
            config.Mode,
            config.ConnectionId,
            config.DisplayName,
            config.ScimBearerToken,
            domains,
            BuildOidcConfiguration(config),
            BuildSamlConfiguration(config));
    }

    private static OrganizationOidcConfigurationRecord? BuildOidcConfiguration(OrganizationSsoConfigRow config)
        => string.IsNullOrWhiteSpace(config.OidcIssuer) &&
           string.IsNullOrWhiteSpace(config.OidcClientId) &&
           string.IsNullOrWhiteSpace(config.OidcClientSecret)
            ? null
            : new OrganizationOidcConfigurationRecord(
                config.OidcIssuer ?? string.Empty,
                config.OidcClientId ?? string.Empty,
                config.OidcClientSecret ?? string.Empty);

    private static OrganizationSamlConfigurationRecord? BuildSamlConfiguration(OrganizationSsoConfigRow config)
        => string.IsNullOrWhiteSpace(config.SamlSsoUrl) &&
           string.IsNullOrWhiteSpace(config.SamlCaCertificatePem) &&
           string.IsNullOrWhiteSpace(config.SamlUsernameAttribute) &&
           string.IsNullOrWhiteSpace(config.SamlEmailAttribute) &&
           string.IsNullOrWhiteSpace(config.SamlGroupsAttribute) &&
           string.IsNullOrWhiteSpace(config.SamlEntityIssuer) &&
           string.IsNullOrWhiteSpace(config.SamlSsoIssuer) &&
           string.IsNullOrWhiteSpace(config.SamlGroupsDelimiter) &&
           string.IsNullOrWhiteSpace(config.SamlNameIdPolicyFormat)
            ? null
            : new OrganizationSamlConfigurationRecord(
                config.SamlSsoUrl ?? string.Empty,
                config.SamlCaCertificatePem ?? string.Empty,
                config.SamlUsernameAttribute ?? string.Empty,
                config.SamlEmailAttribute ?? string.Empty,
                config.SamlGroupsAttribute,
                config.SamlEntityIssuer,
                config.SamlSsoIssuer,
                config.SamlGroupsDelimiter,
                config.SamlNameIdPolicyFormat);

    public void UpsertScimProvisionedMembership(
        string orgLogin,
        string externalId,
        string email,
        string? displayName,
        string role,
        bool active)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        var orgId = connection.QuerySingleOrDefault<string>(
            "SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1;",
            new { orgLogin },
            transaction) ?? throw new InvalidOperationException("organization not found");

        var now = DateTimeOffset.UtcNow.ToString("O");
        connection.Execute(
            """
            INSERT INTO org_scim_memberships (
                id,
                org_id,
                email,
                display_name,
                role,
                external_id,
                active,
                created_at,
                updated_at)
            VALUES (
                @id,
                @orgId,
                @email,
                @displayName,
                @role,
                @externalId,
                @active,
                @now,
                @now)
            ON CONFLICT(org_id, external_id) DO UPDATE SET
                email = excluded.email,
                display_name = excluded.display_name,
                role = excluded.role,
                active = excluded.active,
                updated_at = excluded.updated_at;
            """,
            new
            {
                id = "scim-" + Guid.NewGuid().ToString("N"),
                orgId,
                email = NormalizeEmail(email),
                displayName,
                role = NormalizeOrgRole(role),
                externalId,
                active = active ? 1 : 0,
                now
            },
            transaction);
        transaction.Commit();
    }

    public bool DeactivateScimProvisionedMembership(string orgLogin, string externalId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginImmediateTransaction();
        var orgId = connection.QuerySingleOrDefault<string>(
            "SELECT id FROM orgs WHERE login = @orgLogin LIMIT 1;",
            new { orgLogin },
            transaction);
        if (string.IsNullOrWhiteSpace(orgId))
        {
            transaction.Commit();
            return false;
        }

        connection.Execute(
            """
            UPDATE org_scim_memberships
            SET active = 0,
                updated_at = @updatedAt
            WHERE org_id = @orgId
              AND external_id = @externalId;
            """,
            new { orgId, externalId, updatedAt = DateTimeOffset.UtcNow.ToString("O") },
            transaction);

        var claimedUserId = connection.QuerySingleOrDefault<string>(
            """
            SELECT claimed_user_id
            FROM org_scim_memberships
            WHERE org_id = @orgId
              AND external_id = @externalId
            LIMIT 1;
            """,
            new { orgId, externalId },
            transaction);

        if (!string.IsNullOrWhiteSpace(claimedUserId))
        {
            connection.Execute(
                """
                DELETE FROM org_memberships
                WHERE org_id = @orgId
                  AND user_id = @userId;
                """,
                new { orgId, userId = claimedUserId },
                transaction);
        }

        transaction.Commit();
        return true;
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

    private static string NormalizeOrgRole(string role)
        => string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "member";

    private static string NormalizeEmail(string email)
        => email.Trim().ToLowerInvariant();

    private static string NormalizeDomain(string domain)
        => domain.Trim().ToLowerInvariant();

    private static string NormalizeSsoMode(string mode)
        => mode switch
        {
            var value when string.Equals(value, "required", StringComparison.OrdinalIgnoreCase) => "required",
            var value when string.Equals(value, "exclusive", StringComparison.OrdinalIgnoreCase) => "exclusive",
            _ => "optional"
        };

    private static string NormalizeSsoProtocol(string protocol)
        => protocol switch
        {
            var value when string.Equals(value, "saml", StringComparison.OrdinalIgnoreCase) => "saml",
            _ => "oidc"
        };

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

    internal sealed class ClaimableMembershipRow
    {
        public required string OrgId { get; init; }
        public required string Role { get; init; }
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

    internal class OrganizationSsoConfigRow
    {
        public required string Protocol { get; init; }
        public required string Mode { get; init; }
        public string? ConnectionId { get; init; }
        public string? DisplayName { get; init; }
        public string? ScimBearerToken { get; init; }
        public string? OidcIssuer { get; init; }
        public string? OidcClientId { get; init; }
        public string? OidcClientSecret { get; init; }
        public string? SamlSsoUrl { get; init; }
        public string? SamlCaCertificatePem { get; init; }
        public string? SamlUsernameAttribute { get; init; }
        public string? SamlEmailAttribute { get; init; }
        public string? SamlGroupsAttribute { get; init; }
        public string? SamlEntityIssuer { get; init; }
        public string? SamlSsoIssuer { get; init; }
        public string? SamlGroupsDelimiter { get; init; }
        public string? SamlNameIdPolicyFormat { get; init; }
    }

    internal sealed class OrganizationSsoLookupRow : OrganizationSsoConfigRow
    {
        public required string Login { get; init; }
    }
}
