namespace Makana.Api.Endpoints;

internal static class PolicyEndpoints
{
    public static IEndpointRouteBuilder MapPolicyEndpoints(this IEndpointRouteBuilder app)
    {
        var orgPolicies = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");
        orgPolicies.MapMethods("/policygroups/{policyGroup}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgPolicies.MapMethods("/policypacks/{policyPack}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgPolicies.MapMethods("/policypacks/{policyPack}/versions/{version}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgPolicies.MapMethods("/policygroups/{policyGroup}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgPolicies.MapMethods("/policypacks/{policyPack}/latest", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgPolicies.MapMethods("/policypacks/{policyPack}/versions/{version}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgPolicies.MapMethods("/policygroups", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgPolicies.MapMethods("/policypacks/{policyPack}/latest", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        var stackPolicies = app.MapGroup(StackRouteConventions.ApiStackRoute)
            .RequireAuthorization("OrgRouteAccess");
        stackPolicies.MapMethods("/policygroups", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackPolicies.MapMethods("/policypacks", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
