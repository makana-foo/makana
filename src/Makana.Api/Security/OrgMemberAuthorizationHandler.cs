using Makana.Api.Storage;
using Microsoft.AspNetCore.Authorization;

namespace Makana.Api.Security;

internal sealed class OrgMemberAuthorizationHandler(
    IdentityRepository identityRepository,
    AuthenticatedApiContextAccessor authenticatedApiContextAccessor)
    : AuthorizationHandler<OrgMemberRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OrgMemberRequirement requirement)
    {
        if (context is not { Resource: HttpContext { Request.RouteValues: var routeValues } }
         || routeValues["org"] is not string org)
        {
            return Task.CompletedTask;
        }

        var caller = authenticatedApiContextAccessor.GetCurrent();
        if (string.IsNullOrWhiteSpace(caller.UserId))
        {
            return Task.CompletedTask;
        }

        if (!string.IsNullOrWhiteSpace(caller.OrgId))
        {
            var routeOrgId = identityRepository.GetOrgIdByLogin(org);
            if (string.Equals(routeOrgId, caller.OrgId, StringComparison.Ordinal))
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }

        if (string.Equals(identityRepository.GetUserIdByLogin(org), caller.UserId, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (identityRepository.IsUserMemberOfOrgLogin(caller.UserId, org))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
