using Makana.WebConsole.Services;

namespace Makana.WebConsole.Endpoints;

internal static class StackLiveUpdateEndpoints
{
    public static IEndpointRouteBuilder MapStackLiveUpdateEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/live-updates/session-token", HandleGetSessionTokenAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> HandleGetSessionTokenAsync(
        HttpContext httpContext,
        WebConsoleApiTokenAccessor tokenAccessor,
        CancellationToken cancellationToken)
    {
        var tokenInfo = await tokenAccessor.GetApiTokenInfoAsync(cancellationToken);
        if (tokenInfo is null || string.IsNullOrWhiteSpace(tokenInfo.AccessToken))
        {
            return Results.Unauthorized();
        }

        httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        httpContext.Response.Headers.Pragma = "no-cache";

        return Results.Json(new LiveUpdateSessionTokenResponse(
            tokenInfo.AccessToken,
            tokenInfo.ExpiresAt?.ToString("O")));
    }

    private sealed record LiveUpdateSessionTokenResponse(string AccessToken, string? ExpiresAt);
}
