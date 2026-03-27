namespace Makana.Api.Endpoints;

internal static class OrganizationsEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationsEndpoints(this IEndpointRouteBuilder app)
    {
        var orgGroup = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        orgGroup.MapMethods("/members/{username}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgGroup.MapMethods("/teams/{teamName}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgGroup.MapMethods("/teams/{team}/tokens/{tokenId}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgGroup.MapMethods("/members", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgGroup.MapMethods("/teams", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgGroup.MapMethods("/teams/{teamName}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgGroup.MapMethods("/teams/{team}/tokens", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgGroup.MapMethods("/members/{username}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgGroup.MapMethods("/teams/{team}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgGroup.MapMethods("/members/{username}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgGroup.MapMethods("/teams/{teamType}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgGroup.MapMethods("/teams/{team}/tokens", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        return app;
    }
}
