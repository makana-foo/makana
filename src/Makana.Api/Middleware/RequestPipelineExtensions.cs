using Makana.Compatibility;

namespace Makana.Api.Middleware;

internal static class RequestPipelineExtensions
{
    public static IApplicationBuilder UsePulumiMediaType(this IApplicationBuilder app)
    {
        return app.Use(static (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                return next();
            }

            if (!AcceptsPulumiMediaType(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                return Task.CompletedTask;
            }

            return next();
        });

        static bool AcceptsPulumiMediaType(HttpRequest request)
            => request.Headers.Accept == PulumiHeaders.Accept
            || request.Headers.Accept == "*/*";
    }
}
