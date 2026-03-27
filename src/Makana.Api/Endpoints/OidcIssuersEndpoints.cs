namespace Makana.Api.Endpoints;

internal static class OidcIssuersEndpoints
{
    public static IEndpointRouteBuilder MapOidcIssuersEndpoints(this IEndpointRouteBuilder app)
    {
        var oidcIssuers = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        oidcIssuers.MapMethods("/oidc/issuers/{issuerId}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        oidcIssuers.MapMethods("/auth/policies/oidcissuers/{issuerId}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        oidcIssuers.MapMethods("/oidc/issuers", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        oidcIssuers.MapMethods("/oidc/issuers/{issuerId}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        oidcIssuers.MapMethods("/auth/policies/{policyId}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        oidcIssuers.MapMethods("/oidc/issuers/{issuerId}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        oidcIssuers.MapMethods("/oidc/issuers", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        oidcIssuers.MapMethods("/oidc/issuers/{issuerId}/regenerate-thumbprints", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        return app;
    }
}
