using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class PolicyResultsEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapPolicyResultsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/orgs/{organization}/policyresults/issues/{issueId}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/policyresults/violationsv2", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/policyresults/issues/{issueId}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/policyresults/issues", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
