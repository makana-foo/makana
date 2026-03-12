namespace Makana.Api.Endpoints;

internal static class EnvironmentsEndpoints
{
    public static IEndpointRouteBuilder MapEnvironmentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/esc/environments/{organization}/{project}/{environment}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/esc/environments/{organization}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/esc/environments/{organization}/{project}/{environment}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/esc/environments/{organization}/{project}/{environment}/open/{openSessionID}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/esc/environments/{organization}/{project}/{environment}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/esc/environments/{organization}/{project}/{environment}/settings", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/esc/environments/{organization}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/esc/environments/{organization}/{project}/{environment}/clone", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/esc/environments/{organization}/{project}/{environment}/open", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
