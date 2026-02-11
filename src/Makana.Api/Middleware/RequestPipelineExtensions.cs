using System.IO.Compression;

namespace Makana.Api.Middleware;

internal static class RequestPipelineExtensions
{
    public static IApplicationBuilder UsePulumiMediaType(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!AcceptsPulumiMediaType(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status406NotAcceptable;
                return;
            }

            await next();
        });
    }

    public static IApplicationBuilder UseGzipRequestDecompression(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (context.Request.Headers.TryGetValue("Content-Encoding", out var encoding) &&
                encoding.ToString().Contains("gzip", StringComparison.OrdinalIgnoreCase))
            {
                context.Request.Body = new GZipStream(context.Request.Body, CompressionMode.Decompress, leaveOpen: false);
            }

            await next();
        });
    }

    private static bool AcceptsPulumiMediaType(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Accept", out var accept))
        {
            return true;
        }

        var value = accept.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Contains("application/vnd.pulumi+8", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (value.Contains("*/*", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
