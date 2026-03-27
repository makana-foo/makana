using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Makana.Api.Security;

internal sealed class ApiTokenAuthorizationHandler(AuthenticatedApiContextAccessor authenticatedApiContextAccessor) : AuthorizationHandler<ApiTokenRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ApiTokenRequirement requirement)
    {
        var caller = authenticatedApiContextAccessor.GetCurrent();
        if (!string.IsNullOrWhiteSpace(caller.ApiToken))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
