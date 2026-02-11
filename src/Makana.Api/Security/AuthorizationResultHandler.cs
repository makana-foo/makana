using Makana.Api.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Makana.Api.Security;

internal sealed class MakanaAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _fallback = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden &&
            context.Request.Path.StartsWithSegments("/api/stacks", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(
                new ErrorResponse(404, "Organization not found", null),
                SecurityJsonContext.Default.ErrorResponse);
            return;
        }

        await _fallback.HandleAsync(next, context, policy, authorizeResult);
    }
}
