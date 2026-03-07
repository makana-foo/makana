using Makana.WebConsole.Services;

namespace Makana.WebConsole.Endpoints;

internal static class AvatarEndpoints
{
    public static IEndpointRouteBuilder MapAvatarEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/avatars/{userId}.svg", HandleAvatar)
            .RequireAuthorization();

        return app;
    }

    private static IResult HandleAvatar(HttpContext context, string userId, UserAvatarService avatarService)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.NotFound();
        }

        var svg = avatarService.GetSvg(userId, context.User.Identity?.Name);
        context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.Text(svg, "image/svg+xml; charset=utf-8");
    }
}
