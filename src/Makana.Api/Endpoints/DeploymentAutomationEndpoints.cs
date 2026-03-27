namespace Makana.Api.Endpoints;

internal static class DeploymentAutomationEndpoints
{
    public static IEndpointRouteBuilder MapDeploymentAutomationEndpoints(this IEndpointRouteBuilder app)
    {
        var orgDeployments = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");
        orgDeployments.MapMethods("/deployments", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgDeployments.MapMethods("/deployments/metadata", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgDeployments.MapMethods("/deployments/pause", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgDeployments.MapMethods("/deployments/resume", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        var stackDeployments = app.MapGroup(StackRouteConventions.ApiStackRoute)
            .RequireAuthorization("OrgRouteAccess");
        stackDeployments.MapMethods("/deployments/schedules/{scheduleID}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackDeployments.MapMethods("/deployments/metadata", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackDeployments.MapMethods("/deployments/schedules", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackDeployments.MapMethods("/deployments/schedules/{scheduleID}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackDeployments.MapMethods("/deployments/schedules/{scheduleID}/history", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackDeployments.MapMethods("/deployments/version/{deploymentVersion}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackDeployments.MapMethods("/deployments/{deploymentID}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackDeployments.MapMethods("/deployments/drift/schedules", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/drift/schedules/{scheduleID}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/pause", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/resume", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/schedules", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/schedules/{scheduleID}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/schedules/{scheduleID}/pause", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/schedules/{scheduleID}/resume", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/settings", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/ttl/schedules", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/ttl/schedules/{scheduleID}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackDeployments.MapMethods("/deployments/{deploymentID}/cancel", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        return app;
    }
}
