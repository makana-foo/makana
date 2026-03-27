namespace Makana.Api.Endpoints;

internal static class DataExportEndpoints
{
    public static IEndpointRouteBuilder MapDataExportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess")
            .MapMethods("/search/resources/export", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
