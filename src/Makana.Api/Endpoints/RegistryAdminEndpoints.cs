namespace Makana.Api.Endpoints;

internal static class RegistryAdminEndpoints
{
    public static IEndpointRouteBuilder MapRegistryAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api")
            .RequireAuthorization("ApiToken");

        admin.MapMethods("/preview/registry/packages/{source}/{publisher}/{name}/versions/{version}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        admin.MapMethods("/registry/templates/{source}/{publisher}/{name}/versions/{version}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
