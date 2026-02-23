using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Makana.Api.Endpoints;

internal static class CoreEndpoints
{
    public static IEndpointRouteBuilder MapCoreEndpoints(
        this IEndpointRouteBuilder app,
        BackendStore store,
        OidcSettings oidcSettings)
    {
        app.MapGet("/api/user", async (HttpContext context) =>
        {
            var resolved = await ResolveIdentityAsync(context, store, oidcSettings.ProviderKey);
            if (resolved is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(resolved.Value.User);
        });

        app.MapGet("/api/user/stacks", async (HttpContext context, HttpRequest request, [FromQuery] string? project, [FromQuery(Name = "organization")] string? organization) =>
        {
            var resolved = await ResolveIdentityAsync(context, store, oidcSettings.ProviderKey);
            if (resolved is null)
            {
                return Results.Unauthorized();
            }

            var userId = resolved.Value.User.Id;
            var userLogin = resolved.Value.User.GitHubLogin;
            var namespaceIds = await ResolveVisibleNamespaceIdsAsync(
                context,
                store,
                userId,
                userLogin,
                organization);
            if (namespaceIds.Count == 0)
            {
                return Results.Ok(new ListStacksResponse(Array.Empty<StackSummary>(), null));
            }

            var rows = await store.ListStacksByNamespaceIdsDetailedAsync(project, namespaceIds);
            var stacks = new List<StackSummary>(rows.Count);
            foreach (var row in rows)
            {
                var isPersonal = string.Equals(row.NamespaceId, userId, StringComparison.Ordinal);
                var displayOrg = isPersonal
                    ? userLogin
                    : NamespaceRoutingHelpers.IsLoginCollision(row.OrgName, userLogin)
                        ? NamespaceRoutingHelpers.GetCollisionOrgAlias(row.OrgName, row.NamespaceId)
                        : row.OrgName;
                var tags = row.TagsJson is null
                    ? null
                    : JsonSerializer.Deserialize(row.TagsJson, EndpointHelpersJsonContext.Default.DictionaryStringString);
                var lastUpdate = await store.GetLatestUpdateTimeAsync(row.StackId);
                var resourceCount = await EndpointHelpers.GetResourceCountAsync(store, row.StackId);
                var link = new StackLinks($"{EndpointHelpers.GetBaseUrl(request)}/stacks/{displayOrg}/{row.ProjectName}/{row.Name}");
                stacks.Add(new StackSummary(
                    $"{displayOrg}/{row.ProjectName}/{row.Name}",
                    row.Name,
                    row.ProjectName,
                    displayOrg,
                    EndpointHelpers.ToUnixTimeSeconds(lastUpdate),
                    resourceCount,
                    link,
                    tags));
            }

            return Results.Ok(new ListStacksResponse(stacks.ToArray(), null));
        });

        app.MapGet("/api/user/organizations/default", async (HttpContext context) =>
        {
            var resolved = await ResolveIdentityAsync(context, store, oidcSettings.ProviderKey);
            if (resolved is null)
            {
                return Results.Unauthorized();
            }

            var response = new GetDefaultOrganizationResponse(
                resolved.Value.DefaultOrg,
                Array.Empty<Message>());
            return Results.Ok(response);
        });

        app.MapGet("/api/cli/version", () =>
        {
            const string version = "v3.217.1";
            var response = new CliVersionResponse(version, version, version);
            return Results.Ok(response);
        }).AllowAnonymous();

        app.MapPost("/api/oauth/token", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var grantType = form["grant_type"].ToString();
            var subjectTokenType = form["subject_token_type"].ToString();
            var requestedTokenType = form["requested_token_type"].ToString();
            var scope = form["scope"].ToString();
            var subjectToken = form["subject_token"].ToString();
            var audience = form["audience"].ToString();

            if (!string.Equals(grantType, "urn:ietf:params:oauth:grant-type:token-exchange", StringComparison.Ordinal) ||
                !string.Equals(subjectTokenType, "urn:ietf:params:oauth:token-type:id_token", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(subjectToken))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid token exchange request", null));
            }

            if (string.IsNullOrWhiteSpace(oidcSettings.Authority))
            {
                return Results.Problem("OIDC_AUTHORITY is not configured for token exchange.", statusCode: 500);
            }

            var validatedPrincipal = await ValidateSubjectTokenAsync(
                oidcSettings.Authority,
                oidcSettings.MetadataAddress,
                subjectToken,
                context.RequestAborted);
            if (validatedPrincipal is null)
            {
                return Results.Unauthorized();
            }

            var requestedOrg = TryParseAudienceOrg(audience);
            (string UserId, string? OrgId) resolved;
            try
            {
                resolved = await EnsureIdentityFromClaimsAsync(
                    validatedPrincipal,
                    store,
                    requestedOrg,
                    oidcSettings.ProviderKey);
            }
            catch
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid token claims", null));
            }

            var expiresIn = ParseExpirationSeconds(form["expiration"].ToString());
            var accessToken = "token-" + Guid.NewGuid().ToString("N");
            await store.EnsureApiTokenAsync(
                accessToken,
                resolved.UserId,
                resolved.OrgId,
                DateTimeOffset.UtcNow.AddSeconds(expiresIn));

            var response = new TokenExchangeGrantResponse(
                AccessToken: accessToken,
                IssuedTokenType: requestedTokenType,
                TokenType: "bearer",
                ExpiresIn: expiresIn,
                Scope: scope,
                RefreshToken: null
            );
            return Results.Ok(response);
        }).AllowAnonymous();

        app.MapPost("/api/ai/template", () => Results.NoContent());
        app.MapPost("/api/ai/chat/preview", () => Results.NoContent());

        app.MapGet("/api/capabilities", () => Results.Ok(EndpointHelpers.GetCapabilitiesResponse()));

        return app;
    }

    private static async Task<ResolvedUser?> ResolveIdentityAsync(
        HttpContext context,
        BackendStore store,
        string providerKey)
    {
        var authKind = context.User.FindFirst(PulumiAuthenticationDefaults.AuthKindClaim)?.Value;
        var userId = context.User.FindFirst(PulumiAuthenticationDefaults.UserIdClaim)?.Value;
        var tokenOrgId = context.User.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;

        if (string.Equals(authKind, "token", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }
            return await BuildResolvedUserAsync(store, userId, tokenOrgId);
        }

        if (string.Equals(authKind, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var ensured = await EnsureIdentityFromClaimsAsync(
                    context.User,
                    store,
                    requestedOrgLogin: null,
                    providerKey);
                return await BuildResolvedUserAsync(store, ensured.UserId, ensured.OrgId);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static async Task<ResolvedUser?> BuildResolvedUserAsync(BackendStore store, string userId, string? preferredOrgId)
    {
        var user = await store.GetUserByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var orgs = await store.GetUserOrgsAsync(userId);
        var defaultOrgLogin = !string.IsNullOrWhiteSpace(preferredOrgId)
            ? await store.GetOrgLoginByIdAsync(preferredOrgId!)
            : null;
        // Personal namespace should remain the default owner for the CLI.
        if (string.IsNullOrWhiteSpace(defaultOrgLogin) ||
            string.Equals(defaultOrgLogin, user.Value.Login, StringComparison.OrdinalIgnoreCase))
        {
            defaultOrgLogin = user.Value.Login;
        }

        var orgInfos = orgs
            .Select(org =>
            {
                var displayLogin = NamespaceRoutingHelpers.IsLoginCollision(org.Login, user.Value.Login)
                    ? NamespaceRoutingHelpers.GetCollisionOrgAlias(org.Login, org.Id)
                    : org.Login;
                return new ServiceUserInfo(
                    Name: displayLogin,
                    GitHubLogin: displayLogin,
                    AvatarUrl: string.Empty,
                    Email: null);
            })
            .ToArray();

        var serviceUser = new ServiceUser(
            Id: user.Value.Id,
            GitHubLogin: user.Value.Login,
            Name: user.Value.Name,
            Email: user.Value.Email ?? string.Empty,
            AvatarUrl: user.Value.AvatarUrl,
            Organizations: orgInfos,
            Identities: Array.Empty<string>(),
            SiteAdmin: null,
            TokenInfo: null
        );

        return new ResolvedUser(serviceUser, defaultOrgLogin);
    }

    private static async Task<IReadOnlyCollection<string>> ResolveVisibleNamespaceIdsAsync(
        HttpContext context,
        BackendStore store,
        string userId,
        string userLogin,
        string? organizationFilter)
    {
        var tokenOrgId = context.User.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(tokenOrgId))
        {
            var tokenOrgLogin = await store.GetOrgLoginByIdAsync(tokenOrgId);
            if (string.IsNullOrWhiteSpace(tokenOrgLogin))
            {
                return Array.Empty<string>();
            }

            var tokenOrgAlias = NamespaceRoutingHelpers.IsLoginCollision(tokenOrgLogin, userLogin)
                ? NamespaceRoutingHelpers.GetCollisionOrgAlias(tokenOrgLogin, tokenOrgId)
                : null;

            if (!string.IsNullOrWhiteSpace(organizationFilter))
            {
                if (string.Equals(organizationFilter, tokenOrgLogin, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(tokenOrgAlias) &&
                     string.Equals(organizationFilter, tokenOrgAlias, StringComparison.OrdinalIgnoreCase)))
                {
                    return new[] { tokenOrgId };
                }

                if (string.Equals(organizationFilter, userLogin, StringComparison.OrdinalIgnoreCase))
                {
                    return new[] { userId };
                }

                return Array.Empty<string>();
            }

            return string.Equals(tokenOrgLogin, userLogin, StringComparison.OrdinalIgnoreCase)
                ? new[] { userId, tokenOrgId }
                : new[] { tokenOrgId };
        }

        var namespaceIds = new HashSet<string>(StringComparer.Ordinal) { userId };
        var orgs = await store.GetUserOrgsAsync(userId);

        if (string.IsNullOrWhiteSpace(organizationFilter))
        {
            foreach (var org in orgs)
            {
                namespaceIds.Add(org.Id);
            }

            return namespaceIds.ToArray();
        }

        if (string.Equals(userLogin, organizationFilter, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { userId };
        }

        foreach (var org in orgs)
        {
            if (string.Equals(org.Login, organizationFilter, StringComparison.OrdinalIgnoreCase))
            {
                return new[] { org.Id };
            }

            if (NamespaceRoutingHelpers.IsLoginCollision(org.Login, userLogin) &&
                string.Equals(
                    NamespaceRoutingHelpers.GetCollisionOrgAlias(org.Login, org.Id),
                    organizationFilter,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new[] { org.Id };
            }
        }

        return Array.Empty<string>();
    }

    private static async Task<(string UserId, string? OrgId)> EnsureIdentityFromClaimsAsync(
        ClaimsPrincipal principal,
        BackendStore store,
        string? requestedOrgLogin,
        string providerKey)
    {
        var login = BuildLogin(principal);
        var userId = OidcClaimHelpers.ResolveCanonicalUserId(principal, providerKey)
            ?? throw new InvalidOperationException("Missing subject claim.");
        var displayName = GetClaim(principal, "name", ClaimTypes.Name) ?? login;
        var email = GetClaim(principal, "email", ClaimTypes.Email);
        var avatar = GetClaim(principal, "picture") ?? string.Empty;

        await store.EnsureUserAsync(userId, login, displayName, email, avatar);

        if (string.IsNullOrWhiteSpace(requestedOrgLogin))
        {
            return (userId, null);
        }

        var orgId = await store.GetOrgIdByLoginAsync(requestedOrgLogin!);
        if (string.IsNullOrWhiteSpace(orgId))
        {
            throw new InvalidOperationException("Organization not found");
        }

        if (!await store.IsUserMemberOfOrgLoginAsync(userId, requestedOrgLogin!) &&
            !string.Equals(login, requestedOrgLogin, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Organization not found");
        }

        return (userId, orgId);
    }

    private static async Task<ClaimsPrincipal?> ValidateSubjectTokenAsync(
        string oidcAuthority,
        string? metadataAddress,
        string subjectToken,
        CancellationToken cancellationToken)
    {
        var metadata = string.IsNullOrWhiteSpace(metadataAddress)
            ? oidcAuthority.TrimEnd('/') + "/.well-known/openid-configuration"
            : metadataAddress;
        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            metadata,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = false });

        try
        {
            var config = await configManager.GetConfigurationAsync(cancellationToken);
            var tokenHandler = new JwtSecurityTokenHandler();
            var parameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = config.Issuer,
                ValidateAudience = false,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = tokenHandler.ValidateToken(subjectToken, parameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryParseAudienceOrg(string audience)
    {
        const string prefix = "urn:pulumi:org:";
        if (audience.StartsWith(prefix, StringComparison.Ordinal))
        {
            return audience[prefix.Length..];
        }

        return null;
    }

    internal readonly record struct OidcSettings(
        string ProviderKey,
        string Authority,
        string? MetadataAddress);

    private static int ParseExpirationSeconds(string raw)
    {
        if (!int.TryParse(raw, out var parsed))
        {
            return 3600;
        }

        return Math.Clamp(parsed, 60, 86400);
    }

    private static string BuildLogin(ClaimsPrincipal principal)
    {
        var assertedLogin = GetClaim(
            principal,
            "preferred_username",
            "username",
            "login",
            "nickname",
            ClaimTypes.Upn,
            ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(assertedLogin))
        {
            throw new InvalidOperationException("Missing OIDC login claim.");
        }

        var sanitized = SanitizeLogin(assertedLogin);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new InvalidOperationException("Invalid OIDC login claim.");
        }

        return sanitized;
    }

    private static string SanitizeLogin(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "user" : result;
    }

    private static string? GetClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var claim = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(claim))
            {
                return claim;
            }
        }

        return null;
    }

    private readonly record struct ResolvedUser(ServiceUser User, string DefaultOrg);
}
