namespace Makana.Api.Endpoints;

internal static class DeploymentRunnersEndpoints
{
    public static IEndpointRouteBuilder MapDeploymentRunnersEndpoints(this IEndpointRouteBuilder app)
    {
        var pools = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        pools.MapMethods("/agent-pools/{poolId}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        pools.MapMethods("/agent-pools", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        pools.MapMethods("/agent-pools/{poolId}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        pools.MapMethods("/agent-pools/{poolId}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        pools.MapMethods("/agent-pools", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        return app;
    }
}
