using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace Makana.Api.Endpoints;

internal static class CoreEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapCoreEndpoints(
        this IEndpointRouteBuilder app,
        BackendStore store,
        OidcSettings oidcSettings,
        OidcUserInfoClient userInfoClient)
    {
        app.MapGet("/api/user", async (HttpContext context) =>
        {
            var resolved = await ResolveIdentityAsync(context, store, userInfoClient, oidcSettings.ProviderKey);
            if (resolved is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(resolved.Value.User);
        });

        app.MapGet("/api/user/stacks", async (HttpContext context, HttpRequest request, [FromQuery] string? project, [FromQuery(Name = "organization")] string? organization, EndpointHelpers endpointHelpers) =>
        {
            var resolved = await ResolveIdentityAsync(context, store, userInfoClient, oidcSettings.ProviderKey);
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
                var displayOrg = row.OrgName;
                var tags = row.TagsJson is null
                    ? null
                    : JsonSerializer.Deserialize(row.TagsJson, EndpointHelpersJsonContext.Default.DictionaryStringString);
                var lastUpdate = await store.GetLatestUpdateTimeAsync(row.StackId);
                var resourceCount = await endpointHelpers.GetResourceCountAsync(store, row.StackId);
                var link = new StackLinks($"{endpointHelpers.GetBaseUrl(request)}/stacks/{displayOrg}/{row.ProjectName}/{row.Name}");
                stacks.Add(new StackSummary(
                    $"{displayOrg}/{row.ProjectName}/{row.Name}",
                    row.Name,
                    row.ProjectName,
                    displayOrg,
                    endpointHelpers.ToUnixTimeSeconds(lastUpdate),
                    resourceCount,
                    link,
                    tags));
            }

            return Results.Ok(new ListStacksResponse(stacks.ToArray(), null));
        });

        app.MapGet("/api/user/organizations/default", async (HttpContext context) =>
        {
            var resolved = await ResolveIdentityAsync(context, store, userInfoClient, oidcSettings.ProviderKey);
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

            var subjectTokenAuth = await context.AuthenticateAsync(PulumiAuthenticationDefaults.OidcSubjectTokenScheme);
            if (!subjectTokenAuth.Succeeded || subjectTokenAuth.Principal is null)
            {
                return Results.Unauthorized();
            }

            var requestedOrg = TryParseAudienceOrg(audience);
            (string UserId, string? OrgId) resolved;
            try
            {
                resolved = await EnsureIdentityFromClaimsAsync(
                    subjectTokenAuth.Principal,
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
                TokenType: "Bearer",
                ExpiresIn: expiresIn,
                Scope: scope,
                RefreshToken: null
            );
            return Results.Ok(response);
        }).AllowAnonymous();

        app.MapPost("/api/ai/template", () => Results.NoContent());
        app.MapPost("/api/ai/chat/preview", () => Results.NoContent());

        app.MapGet("/api/capabilities", (EndpointHelpers endpointHelpers) => Results.Ok(endpointHelpers.GetCapabilitiesResponse()));

        return app;
    }

    private static async Task<ResolvedUser?> ResolveIdentityAsync(
        HttpContext context,
        BackendStore store,
        OidcUserInfoClient userInfoClient,
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
            var principal = context.User;
            if (string.IsNullOrWhiteSpace(OidcClaimHelpers.ResolveLogin(principal)))
            {
                principal = await userInfoClient.TryEnrichPrincipalAsync(
                    context,
                    principal,
                    context.RequestAborted) ?? principal;
            }

            try
            {
                var ensured = await EnsureIdentityFromClaimsAsync(
                    principal,
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

        var orgInfos = new List<ServiceUserInfo>();
        var seenOrgLogins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (seenOrgLogins.Add(user.Value.Login))
        {
            orgInfos.Add(new ServiceUserInfo(
                Name: user.Value.Login,
                GitHubLogin: user.Value.Login,
                AvatarUrl: user.Value.AvatarUrl,
                Email: user.Value.Email));
        }

        foreach (var org in orgs)
        {
            if (!seenOrgLogins.Add(org.Login))
            {
                continue;
            }

            orgInfos.Add(new ServiceUserInfo(
                Name: org.Login,
                GitHubLogin: org.Login,
                AvatarUrl: string.Empty,
                Email: null));
        }

        var serviceUser = new ServiceUser(
            Id: user.Value.Id,
            GitHubLogin: user.Value.Login,
            Name: user.Value.Name,
            Email: user.Value.Email ?? string.Empty,
            AvatarUrl: user.Value.AvatarUrl,
            Organizations: orgInfos.ToArray(),
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

            if (!string.IsNullOrWhiteSpace(organizationFilter))
            {
                if (string.Equals(organizationFilter, tokenOrgLogin, StringComparison.OrdinalIgnoreCase))
                {
                    return [tokenOrgId];
                }
                return Array.Empty<string>();
            }

            return [tokenOrgId];
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
            return [userId];
        }

        foreach (var org in orgs)
        {
            if (string.Equals(org.Login, organizationFilter, StringComparison.OrdinalIgnoreCase))
            {
                return [org.Id];
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
        var canonicalUserId = OidcClaimHelpers.ResolveCanonicalUserId(principal, providerKey)
            ?? throw new InvalidOperationException("Missing subject claim.");
        var userId = await store.GetUserIdByLoginAsync(login) ?? canonicalUserId;
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
        string ProviderKey);

    private static int ParseExpirationSeconds(string raw)
    {
        if (!int.TryParse(raw, out var parsed))
        {
            return 7200;
        }

        return Math.Clamp(parsed, 60, 86400);
    }

    private static string BuildLogin(ClaimsPrincipal principal)
    {
        var login = OidcClaimHelpers.ResolveLogin(principal);
        if (string.IsNullOrWhiteSpace(login))
        {
            throw new InvalidOperationException("Missing OIDC login claim.");
        }

        return login;
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
