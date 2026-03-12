namespace Makana.Api.Endpoints;

internal static class RegistryAdminEndpoints
{
    public static IEndpointRouteBuilder MapRegistryAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/preview/registry/packages/{source}/{publisher}/{name}/versions/{version}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/registry/templates/{source}/{publisher}/{name}/versions/{version}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
