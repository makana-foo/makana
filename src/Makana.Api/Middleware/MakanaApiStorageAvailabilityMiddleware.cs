using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;

namespace Makana.Api.Middleware;

public sealed class MakanaApiStorageAvailabilityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, MakanaApiStorageRuntime storageRuntime)
    {
        if (!storageRuntime.CanServeDatabaseTraffic && IsDatabaseBackedPath(context.Request.Path))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new ErrorResponse(503, storageRuntime.UnavailableMessage, null),
                SecurityJsonContext.Default.ErrorResponse,
                cancellationToken: context.RequestAborted);
            return;
        }

        await next(context);
    }

    private static bool IsDatabaseBackedPath(PathString path)
    {
        return path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/hubs/stack-updates", StringComparison.OrdinalIgnoreCase);
    }
}
