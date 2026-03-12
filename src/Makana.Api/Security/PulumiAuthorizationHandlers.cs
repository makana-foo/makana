using System.Security.Claims;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Authorization;

namespace Makana.Api.Security;

internal sealed class ApiTokenRequirement : IAuthorizationRequirement;

internal sealed class ApiTokenAuthorizationHandler : AuthorizationHandler<ApiTokenRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ApiTokenRequirement requirement)
    {
        var authKind = context.User.FindFirst(PulumiAuthenticationDefaults.AuthKindClaim)?.Value;
        if (string.Equals(authKind, "token", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal sealed class UpdateTokenForRouteRequirement : IAuthorizationRequirement;

internal sealed class UpdateTokenForRouteAuthorizationHandler(UpdateCommandRepository updateCommands)
    : AuthorizationHandler<UpdateTokenForRouteRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        UpdateTokenForRouteRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext)
        {
            return;
        }

        var authKind = context.User.FindFirst(PulumiAuthenticationDefaults.AuthKindClaim)?.Value;
        if (!string.Equals(authKind, "update-token", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var token = context.User.FindFirst(PulumiAuthenticationDefaults.AuthTokenClaim)?.Value;
        var updateId = httpContext.Request.RouteValues["updateId"]?.ToString();
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(updateId))
        {
            return;
        }

        if (updateCommands.ValidateUpdateToken(updateId, token))
        {
            context.Succeed(requirement);
        }
    }
}

internal sealed class OrgRouteAccessRequirement : IAuthorizationRequirement;

internal sealed class OrgRouteAccessAuthorizationHandler(
    NamespaceRoutingHelpers namespaceRoutingHelpers,
    IdentityRepository identityRepository,
    OidcUserInfoClient oidcUserInfoClient,
    IConfiguration configuration)
    : AuthorizationHandler<OrgRouteAccessRequirement>
{
    private readonly string _providerKey = NormalizeProviderKey(configuration["OIDC_PROVIDER"] ?? "oidc");

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrgRouteAccessRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext)
        {
            return;
        }

        var org = httpContext.Request.RouteValues["org"]?.ToString();
        if (string.IsNullOrWhiteSpace(org))
        {
            return;
        }

        var authKind = context.User.FindFirst(PulumiAuthenticationDefaults.AuthKindClaim)?.Value;
        if (string.Equals(authKind, "update-token", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return;
        }

        var principal = context.User;
        if (string.Equals(authKind, "bearer", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(OidcClaimHelpers.ResolveLogin(principal)))
        {
            principal = await oidcUserInfoClient.TryEnrichPrincipalAsync(
                httpContext,
                principal,
                httpContext.RequestAborted) ?? principal;
        }

        var userId = principal.FindFirst(PulumiAuthenticationDefaults.UserIdClaim)?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.Equals(authKind, "bearer", StringComparison.OrdinalIgnoreCase))
        {
            userId = EnsureBearerUserIdentity(principal, userId);
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var tokenOrgId = principal.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;
        var namespaceId = await namespaceRoutingHelpers.ResolveAccessibleNamespaceIdAsync(
            userId,
            org,
            tokenOrgId);
        if (string.IsNullOrWhiteSpace(namespaceId))
        {
            return;
        }

        context.Succeed(requirement);
    }

    private string? EnsureBearerUserIdentity(ClaimsPrincipal principal, string? existingUserId)
    {
        if (principal.Identity is not ClaimsIdentity identity)
        {
            return existingUserId;
        }

        var login = OidcClaimHelpers.ResolveLogin(principal);
        var canonicalUserId = OidcClaimHelpers.ResolveCanonicalUserId(principal, _providerKey);
        var resolvedUserId = !string.IsNullOrWhiteSpace(login)
            ? identityRepository.GetUserIdByLogin(login) ?? existingUserId ?? canonicalUserId
            : existingUserId ?? canonicalUserId;
        if (string.IsNullOrWhiteSpace(resolvedUserId))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(login))
        {
            var displayName = principal.FindFirst("name")?.Value
                ?? principal.FindFirst(ClaimTypes.Name)?.Value
                ?? login;
            var email = principal.FindFirst("email")?.Value
                ?? principal.FindFirst(ClaimTypes.Email)?.Value;
            var avatarUrl = principal.FindFirst("picture")?.Value ?? string.Empty;
            identityRepository.EnsureUser(resolvedUserId, login, displayName, email, avatarUrl);
        }

        if (!identity.HasClaim(c => c.Type == PulumiAuthenticationDefaults.UserIdClaim))
        {
            identity.AddClaim(new Claim(PulumiAuthenticationDefaults.UserIdClaim, resolvedUserId));
        }

        return resolvedUserId;
    }

    private static string NormalizeProviderKey(string provider)
    {
        var value = provider.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(value) ? "oidc" : value;
    }
}
