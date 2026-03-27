using System.Security.Claims;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Authorization;

namespace Makana.Api.Security;

internal sealed class UpdateTokenForRouteAuthorizationHandler(
    UpdateCommandRepository updateCommands,
    AuthenticatedApiContextAccessor authenticatedApiContextAccessor)
    : AuthorizationHandler<UpdateTokenRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, UpdateTokenRequirement requirement)
    {
        var caller = authenticatedApiContextAccessor.GetCurrent();
        if (string.IsNullOrWhiteSpace(caller.UpdateToken))
        {
            return Task.CompletedTask;
        }

        if (context is not { Resource: HttpContext { Request.RouteValues: var routeValues } }
         || routeValues["updateId"] is not string updateId)
        {
            return Task.CompletedTask;
        }

        if (updateCommands.ValidateUpdateToken(updateId, caller.UpdateToken))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
