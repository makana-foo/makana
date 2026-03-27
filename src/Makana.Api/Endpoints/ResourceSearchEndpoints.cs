namespace Makana.Api.Endpoints;

internal static class ResourceSearchEndpoints
{
    public static IEndpointRouteBuilder MapResourceSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var orgResources = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        orgResources.MapMethods("/resources/summary", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgResources.MapMethods("/search/resourcesv2", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
