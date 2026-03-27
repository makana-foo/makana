using Makana.Api.Storage;
using Microsoft.AspNetCore.Authorization;

namespace Makana.Api.Security;

internal sealed class StackUpdateHubAuthorizationHandler(
    IdentityRepository identityRepository,
    StackCatalogQueryRepository stackQueries,
    UpdateCommandRepository updateCommands,
    AuthenticatedApiContextAccessor authenticatedApiContextAccessor)
    : AuthorizationHandler<StackUpdateHubRequirement>
{
    private static readonly PathString StackUpdateHubPath = new("/hubs/stack-updates");

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StackUpdateHubRequirement requirement)
    {
        if (context.Resource is not HttpContext httpContext ||
            !httpContext.Request.Path.StartsWithSegments(StackUpdateHubPath))
        {
            return Task.CompletedTask;
        }

        var org = httpContext.Request.Query["org"].ToString();
        var project = httpContext.Request.Query["project"].ToString();
        var stack = httpContext.Request.Query["stack"].ToString();
        var updateId = httpContext.Request.Query["updateId"].ToString();
        if (string.IsNullOrWhiteSpace(org) ||
            string.IsNullOrWhiteSpace(project) ||
            string.IsNullOrWhiteSpace(stack) ||
            string.IsNullOrWhiteSpace(updateId))
        {
            return Task.CompletedTask;
        }

        var stackIds = stackQueries.GetStackIds(org, project, stack);
        var updateContext = updateCommands.GetUpdateContext(updateId);
        if (stackIds is null ||
            updateContext is null ||
            !string.Equals(stackIds.Value.StackId, updateContext.Value.StackId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        var caller = authenticatedApiContextAccessor.Resolve(context.User);
        if (!string.IsNullOrWhiteSpace(caller.UpdateToken) &&
            updateCommands.ValidateUpdateToken(updateId, caller.UpdateToken))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

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
                return Task.CompletedTask;
            }
        }

        if (string.Equals(identityRepository.GetUserIdByLogin(org), caller.UserId, StringComparison.Ordinal) ||
            identityRepository.IsUserMemberOfOrgLogin(caller.UserId, org))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
