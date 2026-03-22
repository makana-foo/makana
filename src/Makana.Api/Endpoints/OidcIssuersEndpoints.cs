using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class OidcIssuersEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapOidcIssuersEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/orgs/{organization}/oidc/issuers/{issuerId}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/auth/policies/oidcissuers/{issuerId}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/oidc/issuers", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/oidc/issuers/{issuerId}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/auth/policies/{policyId}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/oidc/issuers/{issuerId}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/oidc/issuers", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/oidc/issuers/{issuerId}/regenerate-thumbprints", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
