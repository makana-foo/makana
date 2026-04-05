using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using Makana.Api.Contracts;
using Makana.Api.Configuration;
using Makana.Api.Security;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.AspNetCore.Mvc;

namespace Makana.Api.Endpoints;

internal static class CoreEndpoints
{
    private static readonly ServiceThemeOption[] ThemeOptions =
    [
        new("default", "Default", "/css/themes/default.css"),
        new("island", "Island", "/css/themes/island.css")
    ];
    private const int WebConsoleSessionAccessTokenLifetimeSeconds = 300;
    private const string TokenExchangeGrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string SubjectTokenTypeIdToken = "urn:ietf:params:oauth:token-type:id_token";
    private const string PulumiOrganizationAudiencePrefix = "urn:pulumi:org:";

    public static IEndpointRouteBuilder MapCoreEndpoints(
        this IEndpointRouteBuilder app,
        IdentityRepository identityRepository,
        StackCatalogQueryRepository stackQueries,
        UpdateQueryRepository updateQueries)
    {
        app.MapGet("/api/user", async (AuthenticatedApiContextAccessor authenticatedApiContextAccessor) =>
        {
            var resolved = await ResolveIdentityAsync(authenticatedApiContextAccessor, identityRepository);
            if (resolved is null)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(resolved.Value.User);
        });

        app.MapPut("/api/user/theme", async (UpdateUserThemeRequest request, AuthenticatedApiContextAccessor authenticatedApiContextAccessor) =>
        {
            var resolved = await ResolveIdentityAsync(authenticatedApiContextAccessor, identityRepository);
            if (resolved is null)
            {
                return Results.Unauthorized();
            }

            var normalizedTheme = NormalizeTheme(request.Theme);
            if (normalizedTheme is null)
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid theme", null));
            }

            identityRepository.SetUserTheme(resolved.Value.User.Id, normalizedTheme);
            return Results.NoContent();
        });

        app.MapGet("/api/user/stacks", async (
            HttpRequest request,
            [FromQuery] string? project,
            [FromQuery(Name = "organization")] string? organization,
            EndpointHelpers endpointHelpers,
            AuthenticatedApiContextAccessor authenticatedApiContextAccessor) =>
        {
            var resolved = await ResolveIdentityAsync(authenticatedApiContextAccessor, identityRepository);
            if (resolved is null)
            {
                return Results.Unauthorized();
            }

            var userId = resolved.Value.User.Id;
            var userLogin = resolved.Value.User.GitHubLogin;
            var namespaceIds = await ResolveVisibleNamespaceIdsAsync(
                identityRepository,
                userId,
                userLogin,
                resolved.Value.TokenOrgId,
                organization);
            if (namespaceIds.Count == 0)
            {
                return Results.Ok(new ListStacksResponse([], null));
            }

            var rows = stackQueries.ListStacksByNamespaceIdsDetailed(project, namespaceIds);
            var stacks = new List<StackSummary>(rows.Count);
            foreach (var row in rows)
            {
                var displayOrg = row.OrgName;
                var tags = row.TagsJson is null
                    ? null
                    : JsonSerializer.Deserialize(row.TagsJson, EndpointHelpersJsonContext.Default.DictionaryStringString);
                var lastUpdate = stackQueries.GetLatestUpdateTime(row.StackId);
                var resourceCount = await endpointHelpers.GetResourceCountAsync(updateQueries, row.StackId);
                var link = new StackLinks(endpointHelpers.BuildStackConsoleUrl(request, displayOrg, row.ProjectName, row.Name));
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

            return Results.Ok(new ListStacksResponse([.. stacks], null));
        });

        app.MapGet("/api/user/organizations/default", async (AuthenticatedApiContextAccessor authenticatedApiContextAccessor) =>
        {
            var resolved = await ResolveIdentityAsync(authenticatedApiContextAccessor, identityRepository);
            if (resolved is null)
            {
                return Results.Unauthorized();
            }

            var response = new GetDefaultOrganizationResponse(
                resolved.Value.DefaultOrg,
                []);
            return Results.Ok(response);
        });

        app.MapGet("/api/cli/version", () =>
        {
            const string version = "v3.217.1";
            var response = new CliVersionResponse(version, version, version);
            return Results.Ok(response);
        }).AllowAnonymous();

        app.MapPost("/api/oauth/token", async Task<IResult> (
            HttpRequest request,
            OidcSubjectTokenValidator subjectTokenValidator,
            OidcSubjectTokenValidationOptions validationOptions,
            CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var grantType = form["grant_type"].ToString();
            var subjectTokenType = form["subject_token_type"].ToString();
            var requestedTokenType = form["requested_token_type"].ToString();
            var subjectToken = form["subject_token"].ToString();
            var audience = form["audience"].ToString();
            var scope = form["scope"].ToString();
            var expirationValue = form["expiration"].ToString();

            if (!string.Equals(grantType, TokenExchangeGrantType, StringComparison.Ordinal) ||
                !string.Equals(subjectTokenType, SubjectTokenTypeIdToken, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(requestedTokenType) ||
                string.IsNullOrWhiteSpace(subjectToken))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid token exchange request", null));
            }

            if (!TryResolveTokenExchange(requestedTokenType, audience, scope, out var resolvedExchange))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid token exchange request", null));
            }

            int? requestedExpirationSeconds = null;
            if (!string.IsNullOrWhiteSpace(expirationValue))
            {
                if (!int.TryParse(expirationValue, out var parsedExpirationSeconds) || parsedExpirationSeconds <= 0)
                {
                    return Results.BadRequest(new ErrorResponse(400, "invalid token exchange request", null));
                }

                requestedExpirationSeconds = parsedExpirationSeconds;
            }

            var validatedContext = await ResolveValidatedSubjectTokenAsync(
                subjectToken,
                subjectTokenValidator,
                identityRepository,
                cancellationToken);
            if (validatedContext is null)
            {
                return Results.Unauthorized();
            }

            string? orgId = null;
            string? teamId = null;
            var principalId = validatedContext.Value.LinkedIdentity.UserId;
            var principalType = resolvedExchange.Value.PrincipalType;

            if (!string.IsNullOrWhiteSpace(resolvedExchange.Value.UserScope) &&
                !string.Equals(resolvedExchange.Value.UserScope, validatedContext.Value.User.Login, StringComparison.OrdinalIgnoreCase))
            {
                return Results.Unauthorized();
            }

            if (!string.IsNullOrWhiteSpace(resolvedExchange.Value.OrganizationLogin))
            {
                orgId = identityRepository.GetOrgIdByLogin(resolvedExchange.Value.OrganizationLogin!);
                if (string.IsNullOrWhiteSpace(orgId) ||
                    !identityRepository.IsUserMemberOfOrgLogin(validatedContext.Value.LinkedIdentity.UserId, resolvedExchange.Value.OrganizationLogin!))
                {
                    return Results.Unauthorized();
                }

                principalId = orgId!;
            }

            if (!string.IsNullOrWhiteSpace(resolvedExchange.Value.TeamName))
            {
                if (string.IsNullOrWhiteSpace(orgId))
                {
                    return Results.BadRequest(new ErrorResponse(400, "invalid token exchange request", null));
                }

                var team = identityRepository.GetTeamByOrgAndName(orgId!, resolvedExchange.Value.TeamName!);
                if (team is null || !identityRepository.IsUserMemberOfTeam(validatedContext.Value.LinkedIdentity.UserId, team.Value.Id))
                {
                    return Results.Unauthorized();
                }

                teamId = team.Value.Id;
                principalId = team.Value.Id;
            }

            var now = DateTimeOffset.UtcNow;
            var expiresAt = ResolveExpiresAt(
                now,
                validatedContext.Value.ValidatedSubjectToken.ExpiresAt,
                requestedExpirationSeconds,
                validationOptions.MaxAccessTokenLifetimeSeconds);
            if (expiresAt <= now)
            {
                return Results.Unauthorized();
            }

            var accessToken = CreateOpaqueAccessToken();
            identityRepository.CreateAccessToken(
                accessToken,
                resolvedExchange.Value.TokenType,
                principalType,
                principalId,
                validatedContext.Value.LinkedIdentity.UserId,
                orgId,
                teamId,
                BuildTokenDisplayName(
                    resolvedExchange.Value.TokenType,
                    validatedContext.Value.User.Login,
                    resolvedExchange.Value.OrganizationLogin,
                    resolvedExchange.Value.TeamName,
                    resolvedExchange.Value.HasAdminScope),
                description: null,
                issuedAt: now,
                expiresAt: expiresAt,
                issuedVia: "token-exchange",
                linkedIdentityId: validatedContext.Value.LinkedIdentity.IdentityId,
                createdByUserId: validatedContext.Value.LinkedIdentity.UserId,
                scopeAdmin: resolvedExchange.Value.HasAdminScope);

            var expiresIn = Math.Max(1L, (long)Math.Floor((expiresAt - now).TotalSeconds));
            var issuedTokenType = resolvedExchange.Value.TokenType switch
            {
                PulumiAccessTokenTypes.Team => PulumiAccessTokenTypes.TeamAccessToken,
                PulumiAccessTokenTypes.Organization => PulumiAccessTokenTypes.OrganizationAccessToken,
                _ => PulumiAccessTokenTypes.PersonalAccessToken
            };

            return Results.Ok(new TokenExchangeGrantResponse(
                accessToken,
                issuedTokenType,
                PulumiAccessTokenTypes.BearerTokenType,
                expiresIn,
                scope,
                null));
        }).AllowAnonymous();

        app.MapPost("/api/auth/web-console/token", HandleCreateWebConsoleSessionTokenAsync)
            .AllowAnonymous();

        app.MapPost("/api/ai/template", () => Results.NoContent());
        app.MapPost("/api/ai/chat/preview", () => Results.NoContent());

        app.MapGet("/api/capabilities", (EndpointHelpers endpointHelpers) => Results.Ok(endpointHelpers.GetCapabilitiesResponse()));

        return app;
    }

    private static async Task<Results<BadRequest<ErrorResponse>, UnauthorizedHttpResult, Ok<WebConsoleSessionTokenResponse>>> HandleCreateWebConsoleSessionTokenAsync(
        HttpRequest request,
        IdentityRepository identityRepository,
        OidcSubjectTokenValidator subjectTokenValidator,
        CancellationToken cancellationToken)
    {
        WebConsoleSessionTokenRequest? payload;
        try
        {
            payload = await request.ReadFromJsonAsync(
                CoreEndpointsJsonContext.Default.WebConsoleSessionTokenRequest,
                cancellationToken);
        }
        catch (JsonException)
        {
            return TypedResults.BadRequest(new ErrorResponse(400, "invalid request body", null));
        }

        var idToken = payload?.IdToken;
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return TypedResults.BadRequest(new ErrorResponse(400, "id token is required", null));
        }

        var validatedContext = await ResolveValidatedSubjectTokenAsync(
            idToken,
            subjectTokenValidator,
            identityRepository,
            cancellationToken);
        if (validatedContext is null)
        {
            return TypedResults.Unauthorized();
        }

        var now = DateTimeOffset.UtcNow;
        var expiresAt = ResolveExpiresAt(
            now,
            validatedContext.Value.ValidatedSubjectToken.ExpiresAt,
            WebConsoleSessionAccessTokenLifetimeSeconds,
            WebConsoleSessionAccessTokenLifetimeSeconds);
        if (expiresAt <= now)
        {
            return TypedResults.Unauthorized();
        }

        var accessToken = CreateOpaqueAccessToken();
        identityRepository.CreateAccessToken(
            accessToken,
            PulumiAccessTokenTypes.Personal,
            PulumiAccessTokenTypes.PrincipalUser,
            validatedContext.Value.LinkedIdentity.UserId,
            validatedContext.Value.LinkedIdentity.UserId,
            orgId: null,
            teamId: null,
            displayName: "Web console session",
            description: null,
            issuedAt: now,
            expiresAt: expiresAt,
            issuedVia: "web-console-session",
            linkedIdentityId: validatedContext.Value.LinkedIdentity.IdentityId,
            createdByUserId: validatedContext.Value.LinkedIdentity.UserId,
            scopeAdmin: false);

        return TypedResults.Ok(new WebConsoleSessionTokenResponse(accessToken, expiresAt.ToString("O")));
    }

    private static Task<ResolvedUser?> ResolveIdentityAsync(
        AuthenticatedApiContextAccessor authenticatedApiContextAccessor,
        IdentityRepository identityRepository)
    {
        var caller = authenticatedApiContextAccessor.GetCurrent();
        if (string.IsNullOrWhiteSpace(caller.UserId))
        {
            return Task.FromResult<ResolvedUser?>(null);
        }

        return BuildResolvedUserAsync(identityRepository, caller.UserId, caller.OrgId, caller.ApiToken);
    }

    private static Task<ResolvedUser?> BuildResolvedUserAsync(
        IdentityRepository identityRepository,
        string userId,
        string? preferredOrgId,
        string? apiToken)
    {
        var user = identityRepository.GetUserById(userId);
        if (user is null)
        {
            return Task.FromResult<ResolvedUser?>(null);
        }

        var theme = NormalizeTheme(identityRepository.GetUserTheme(userId)) ?? "default";
        var orgs = identityRepository.GetUserOrgs(userId);
        var defaultOrgLogin = !string.IsNullOrWhiteSpace(preferredOrgId)
            ? identityRepository.GetOrgLoginById(preferredOrgId!)
            : null;
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
            Organizations: [.. orgInfos],
            Theme: theme,
            AvailableThemes: ThemeOptions,
            Identities: [],
            SiteAdmin: null,
            TokenInfo: BuildTokenInfo(identityRepository, apiToken)
        );

        return Task.FromResult<ResolvedUser?>(new ResolvedUser(serviceUser, defaultOrgLogin, preferredOrgId));
    }

    private static async Task<ValidatedSubjectContext?> ResolveValidatedSubjectTokenAsync(
        string subjectToken,
        OidcSubjectTokenValidator subjectTokenValidator,
        IdentityRepository identityRepository,
        CancellationToken cancellationToken)
    {
        var tokenHandler = new JsonWebTokenHandler();
        if (!tokenHandler.CanReadToken(subjectToken))
        {
            return null;
        }

        JsonWebToken jsonWebToken;
        try
        {
            jsonWebToken = tokenHandler.ReadJsonWebToken(subjectToken);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (jsonWebToken.ValidTo == DateTime.MinValue || jsonWebToken.ValidTo <= DateTime.UtcNow)
        {
            return null;
        }

        var validatedSubjectToken = await subjectTokenValidator.ValidateAsync(subjectToken, cancellationToken);
        if (validatedSubjectToken is null)
        {
            return null;
        }

        var login = OidcClaimHelpers.ResolveLogin(validatedSubjectToken.Principal);
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        var displayName = OidcClaimHelpers.ResolveDisplayName(validatedSubjectToken.Principal) ?? login;
        var linkedIdentity = identityRepository.ResolveLinkedIdentity(
            validatedSubjectToken.Issuer,
            validatedSubjectToken.Subject,
            login,
            displayName,
            OidcClaimHelpers.ResolveEmail(validatedSubjectToken.Principal),
            OidcClaimHelpers.ResolveEmailVerified(validatedSubjectToken.Principal));
        var verifiedEmail = OidcClaimHelpers.ResolveEmailVerified(validatedSubjectToken.Principal)
            ? OidcClaimHelpers.ResolveEmail(validatedSubjectToken.Principal)
            : null;
        if (!string.IsNullOrWhiteSpace(verifiedEmail))
        {
            identityRepository.ClaimOrganizationMembershipsForVerifiedEmail(linkedIdentity.UserId, verifiedEmail!);
        }
        var user = identityRepository.GetUserById(linkedIdentity.UserId);
        if (user is null)
        {
            return null;
        }

        return new ValidatedSubjectContext(validatedSubjectToken, linkedIdentity, user.Value);
    }

    private static string? NormalizeTheme(string? theme)
    {
        return theme switch
        {
            "default" or "light" or "dark" => "default",
            "island" or "island-light" or "island-dark" => "island",
            _ => null
        };
    }

    private static Task<IReadOnlyCollection<string>> ResolveVisibleNamespaceIdsAsync(
        IdentityRepository identityRepository,
        string userId,
        string userLogin,
        string? tokenOrgId,
        string? organizationFilter)
    {
        if (!string.IsNullOrWhiteSpace(tokenOrgId))
        {
            var tokenOrgLogin = identityRepository.GetOrgLoginById(tokenOrgId);
            if (string.IsNullOrWhiteSpace(tokenOrgLogin))
            {
                return Task.FromResult<IReadOnlyCollection<string>>([]);
            }

            if (!string.IsNullOrWhiteSpace(organizationFilter))
            {
                if (string.Equals(organizationFilter, tokenOrgLogin, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult<IReadOnlyCollection<string>>([tokenOrgId]);
                }

                return Task.FromResult<IReadOnlyCollection<string>>([]);
            }

            return Task.FromResult<IReadOnlyCollection<string>>([tokenOrgId]);
        }

        var namespaceIds = new HashSet<string>(StringComparer.Ordinal) { userId };
        var orgs = identityRepository.GetUserOrgs(userId);

        if (string.IsNullOrWhiteSpace(organizationFilter))
        {
            foreach (var org in orgs)
            {
                namespaceIds.Add(org.Id);
            }

            return Task.FromResult<IReadOnlyCollection<string>>([.. namespaceIds]);
        }

        if (string.Equals(userLogin, organizationFilter, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyCollection<string>>([userId]);
        }

        foreach (var org in orgs)
        {
            if (string.Equals(org.Login, organizationFilter, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<IReadOnlyCollection<string>>([org.Id]);
            }
        }

        return Task.FromResult<IReadOnlyCollection<string>>([]);
    }

    private static ServiceTokenInfo? BuildTokenInfo(IdentityRepository identityRepository, string? apiToken)
    {
        if (string.IsNullOrWhiteSpace(apiToken))
        {
            return null;
        }

        var tokenRecord = identityRepository.GetApiToken(apiToken);
        if (tokenRecord is null)
        {
            return null;
        }

        var organization = !string.IsNullOrWhiteSpace(tokenRecord.OrgId)
            ? identityRepository.GetOrgLoginById(tokenRecord.OrgId!)
            : null;
        var team = !string.IsNullOrWhiteSpace(tokenRecord.TeamId)
            ? identityRepository.GetTeamNameById(tokenRecord.TeamId!)
            : null;
        var displayName = !string.IsNullOrWhiteSpace(tokenRecord.DisplayName)
            ? tokenRecord.DisplayName!
            : tokenRecord.TokenType switch
            {
                PulumiAccessTokenTypes.Team => "Team token",
                PulumiAccessTokenTypes.Organization => tokenRecord.ScopeAdmin ? "Admin organization token" : "Organization token",
                _ => "Personal token"
            };

        return new ServiceTokenInfo(displayName, organization, team);
    }

    private static bool TryResolveTokenExchange(
        string requestedTokenType,
        string? audience,
        string? scope,
        out ResolvedTokenExchange? resolved)
    {
        resolved = null;

        var scopeTokens = SplitScope(scope);
        var teamScope = GetScopedValue(scopeTokens, "team");
        var userScope = GetScopedValue(scopeTokens, "user");
        var hasBareUserScope = scopeTokens.Contains("user");
        var hasAdminScope = scopeTokens.Contains("admin");
        var organizationLogin = ParseOrganizationAudience(audience);

        string effectiveRequestedTokenType;
        if (string.Equals(requestedTokenType, PulumiAccessTokenTypes.GenericAccessToken, StringComparison.Ordinal))
        {
            effectiveRequestedTokenType = !string.IsNullOrWhiteSpace(teamScope)
                ? PulumiAccessTokenTypes.TeamAccessToken
                : (!string.IsNullOrWhiteSpace(userScope) || hasBareUserScope || string.IsNullOrWhiteSpace(organizationLogin))
                    ? PulumiAccessTokenTypes.PersonalAccessToken
                    : PulumiAccessTokenTypes.OrganizationAccessToken;
        }
        else
        {
            effectiveRequestedTokenType = requestedTokenType;
        }

        if (hasAdminScope &&
            !string.Equals(effectiveRequestedTokenType, PulumiAccessTokenTypes.OrganizationAccessToken, StringComparison.Ordinal))
        {
            return false;
        }

        if ((!string.IsNullOrWhiteSpace(teamScope) &&
             !string.Equals(effectiveRequestedTokenType, PulumiAccessTokenTypes.TeamAccessToken, StringComparison.Ordinal)) ||
            ((!string.IsNullOrWhiteSpace(userScope) || hasBareUserScope) &&
             !string.Equals(effectiveRequestedTokenType, PulumiAccessTokenTypes.PersonalAccessToken, StringComparison.Ordinal)))
        {
            return false;
        }

        resolved = effectiveRequestedTokenType switch
        {
            var value when string.Equals(value, PulumiAccessTokenTypes.PersonalAccessToken, StringComparison.Ordinal)
                => new ResolvedTokenExchange(
                    PulumiAccessTokenTypes.Personal,
                    PulumiAccessTokenTypes.PrincipalUser,
                    null,
                    null,
                    !string.IsNullOrWhiteSpace(userScope) ? userScope : (hasBareUserScope ? string.Empty : null),
                    hasAdminScope),
            var value when string.Equals(value, PulumiAccessTokenTypes.OrganizationAccessToken, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(organizationLogin)
                => new ResolvedTokenExchange(
                    PulumiAccessTokenTypes.Organization,
                    PulumiAccessTokenTypes.PrincipalOrganization,
                    organizationLogin,
                    null,
                    null,
                    hasAdminScope),
            var value when string.Equals(value, PulumiAccessTokenTypes.TeamAccessToken, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(organizationLogin)
                && !string.IsNullOrWhiteSpace(teamScope)
                && !hasAdminScope
                => new ResolvedTokenExchange(
                    PulumiAccessTokenTypes.Team,
                    PulumiAccessTokenTypes.PrincipalTeam,
                    organizationLogin,
                    teamScope,
                    null,
                    false),
            _ => null
        };

        return resolved is not null;
    }

    private static HashSet<string> SplitScope(string? scope)
        => [.. (scope ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static string? GetScopedValue(HashSet<string> scopeTokens, string scopeName)
    {
        var prefix = scopeName + ":";
        foreach (var token in scopeTokens)
        {
            if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return token[prefix.Length..];
            }
        }

        return null;
    }

    private static string? ParseOrganizationAudience(string? audience)
    {
        if (string.IsNullOrWhiteSpace(audience) ||
            !audience.StartsWith(PulumiOrganizationAudiencePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return audience[PulumiOrganizationAudiencePrefix.Length..];
    }

    private static DateTimeOffset ResolveExpiresAt(
        DateTimeOffset now,
        DateTimeOffset upstreamExpiresAt,
        int? requestedExpirationSeconds,
        int maxAccessTokenLifetimeSeconds)
    {
        var cappedRequestedSeconds = requestedExpirationSeconds.HasValue && requestedExpirationSeconds.Value > 0
            ? requestedExpirationSeconds.Value
            : maxAccessTokenLifetimeSeconds;
        var requestedExpiresAt = now.AddSeconds(Math.Min(cappedRequestedSeconds, maxAccessTokenLifetimeSeconds));
        return requestedExpiresAt <= upstreamExpiresAt ? requestedExpiresAt : upstreamExpiresAt;
    }

    private static string CreateOpaqueAccessToken()
        => "pul-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)).ToLowerInvariant();

    private static string BuildTokenDisplayName(
        string tokenType,
        string login,
        string? organizationLogin,
        string? teamName,
        bool hasAdminScope)
    {
        return tokenType switch
        {
            PulumiAccessTokenTypes.Team => $"OIDC team token for {organizationLogin}/{teamName}",
            PulumiAccessTokenTypes.Organization => hasAdminScope
                ? $"OIDC admin organization token for {organizationLogin}"
                : $"OIDC organization token for {organizationLogin}",
            _ => $"OIDC personal token for {login}"
        };
    }

    private readonly record struct ResolvedUser(ServiceUser User, string DefaultOrg, string? TokenOrgId);
    private readonly record struct ValidatedSubjectContext(
        ValidatedOidcSubjectToken ValidatedSubjectToken,
        LinkedIdentityResolution LinkedIdentity,
        (string Id, string Login, string Name, string? Email, string AvatarUrl) User);
    private readonly record struct ResolvedTokenExchange(
        string TokenType,
        string PrincipalType,
        string? OrganizationLogin,
        string? TeamName,
        string? UserScope,
        bool HasAdminScope);
}

internal readonly record struct WebConsoleSessionTokenRequest(
    [property: JsonPropertyName("idToken")] string IdToken);

internal readonly record struct WebConsoleSessionTokenResponse(
    [property: JsonPropertyName("accessToken")] string AccessToken,
    [property: JsonPropertyName("expiresAt")] string ExpiresAt);
