namespace Makana.Api.Endpoints;

internal static class DeploymentAutomationEndpoints
{
    public static IEndpointRouteBuilder MapDeploymentAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/deployments", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/deployments/metadata", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/metadata", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}/history", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/version/{deploymentVersion}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/{deploymentID}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/deployments/pause", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/deployments/resume", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/drift/schedules", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/drift/schedules/{scheduleID}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/pause", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/resume", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}/pause", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/schedules/{scheduleID}/resume", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/settings", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/ttl/schedules", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/ttl/schedules/{scheduleID}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/stacks/{organization}/{project}/{stack}/deployments/{deploymentID}/cancel", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
