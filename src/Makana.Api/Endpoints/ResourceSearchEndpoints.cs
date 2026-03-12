namespace Makana.Api.Endpoints;

internal static class ResourceSearchEndpoints
{
    public static IEndpointRouteBuilder MapResourceSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/orgs/{organization}/resources/summary", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{org}/search/resourcesv2", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
