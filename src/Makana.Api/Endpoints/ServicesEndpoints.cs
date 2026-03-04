using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class ServicesEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/orgs/{organization}/services/{ownerType}/{ownerName}/{serviceName}", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/services/{ownerType}/{ownerName}/{serviceName}/items/{itemType}/{itemName}", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/services", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/services/{ownerType}/{ownerName}/{serviceName}", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/services/{ownerType}/{ownerName}/{serviceName}", new[] { "PATCH" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/services", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/services/{ownerType}/{ownerName}/{serviceName}/items", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
