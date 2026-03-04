using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class DeploymentAutomationEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapDeploymentAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}", new[] { "DELETE" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/deployments", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/deployments/metadata", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/metadata", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}/history", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/version/{deploymentVersion}", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/{deploymentID}", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/deployments/pause", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/deployments/resume", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/drift/schedules", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/drift/schedules/{scheduleID}", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/pause", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/resume", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}/pause", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}/resume", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/settings", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/ttl/schedules", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/ttl/schedules/{scheduleID}", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/{deploymentID}/cancel", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
