using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class RegistryAdminEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapRegistryAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/preview/registry/packages/{source}/{publisher}/{name}/versions/{version}", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/registry/templates/{source}/{publisher}/{name}/versions/{version}", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
