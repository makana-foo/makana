using Makana.Api.Storage;
using Microsoft.AspNetCore.Authorization;

namespace Makana.Api.Security;

internal sealed class OrgAdminAuthorizationHandler(
    IdentityRepository identityRepository,
    AuthenticatedApiContextAccessor authenticatedApiContextAccessor)
    : AuthorizationHandler<OrgAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OrgAdminRequirement requirement)
    {
        if (context.Resource is not HttpContext { Request.RouteValues: var routeValues } ||
            routeValues["org"] is not string org)
        {
            return Task.CompletedTask;
        }

        var caller = authenticatedApiContextAccessor.GetCurrent();
        if (!string.IsNullOrWhiteSpace(caller.OrgId))
        {
            var routeOrgId = identityRepository.GetOrgIdByLogin(org);
            if (string.Equals(routeOrgId, caller.OrgId, StringComparison.Ordinal) &&
                caller.HasAdminScope)
            {
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        if (!string.IsNullOrWhiteSpace(caller.UserId) &&
            identityRepository.IsUserAdminOfOrgLogin(caller.UserId, org))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
