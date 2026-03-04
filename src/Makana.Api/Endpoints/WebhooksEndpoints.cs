using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class WebhooksEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/orgs/{organization}/hooks/{webhookname}", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/hooks/{webhookname}", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/hooks", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/hooks/{webhookname}", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/hooks/{webhookname}/deliveries", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/hooks", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/hooks/{webhookname}", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/hooks/{webhookname}/deliveries", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/hooks/{webhookname}", new[] { "PATCH" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/hooks/{webhookname}", new[] { "PATCH" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/hooks", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/hooks/{webhookname}/ping", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/hooks", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/hooks/{webhookname}/ping", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
