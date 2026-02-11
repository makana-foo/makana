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

internal sealed class UpdateTokenForRouteAuthorizationHandler(BackendStore store)
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

        if (await store.ValidateUpdateTokenAsync(updateId, token))
        {
            context.Succeed(requirement);
        }
    }
}

internal sealed class OrgRouteAccessRequirement : IAuthorizationRequirement;

internal sealed class OrgRouteAccessAuthorizationHandler(BackendStore store)
    : AuthorizationHandler<OrgRouteAccessRequirement>
{
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

        var userId = context.User.FindFirst(PulumiAuthenticationDefaults.UserIdClaim)?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return;
        }

        var tokenOrgId = context.User.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;
        if (!string.IsNullOrWhiteSpace(tokenOrgId))
        {
            var tokenOrgLogin = await store.GetOrgLoginByIdAsync(tokenOrgId);
            if (!string.IsNullOrWhiteSpace(tokenOrgLogin))
            {
                if (string.Equals(tokenOrgLogin, org, StringComparison.OrdinalIgnoreCase))
                {
                    context.Succeed(requirement);
                    return;
                }

                var user = await store.GetUserByIdAsync(userId);
                if (user is not null &&
                    NamespaceRoutingHelpers.IsLoginCollision(tokenOrgLogin, user.Value.Login))
                {
                    var alias = NamespaceRoutingHelpers.GetCollisionOrgAlias(tokenOrgLogin, tokenOrgId);
                    if (string.Equals(alias, org, StringComparison.OrdinalIgnoreCase))
                    {
                        context.Succeed(requirement);
                        return;
                    }
                }
            }
        }

        var userRecord = await store.GetUserByIdAsync(userId);
        if (userRecord is not null && string.Equals(userRecord.Value.Login, org, StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return;
        }

        if (userRecord is null)
        {
            return;
        }

        var memberOrgId = await NamespaceRoutingHelpers.ResolveMemberOrgIdFromRouteAsync(
            store,
            userId,
            userRecord.Value.Login,
            org);
        if (!string.IsNullOrWhiteSpace(memberOrgId))
        {
            context.Succeed(requirement);
            return;
        }

        // Intentionally do not allow first-touch or orphan-org claiming.
        // Org resources must pre-exist and caller must be authorized.
    }
}
