namespace Makana.Api.Endpoints;

internal static class PolicyResultsEndpoints
{
    public static IEndpointRouteBuilder MapPolicyResultsEndpoints(this IEndpointRouteBuilder app)
    {
        var policyResults = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        policyResults.MapMethods("/policyresults/issues/{issueId}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        policyResults.MapMethods("/policyresults/violationsv2", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        policyResults.MapMethods("/policyresults/issues/{issueId}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        policyResults.MapMethods("/policyresults/issues", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        return app;
    }
}
