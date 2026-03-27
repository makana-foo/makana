namespace Makana.Api.Endpoints;

internal static class PersonalAccessTokensEndpoints
{
    public static IEndpointRouteBuilder MapPersonalAccessTokensEndpoints(this IEndpointRouteBuilder app)
    {
        var orgTokens = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");
        orgTokens.MapMethods("/tokens/{tokenId}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgTokens.MapMethods("/tokens", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgTokens.MapMethods("/tokens", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        var userTokens = app.MapGroup("/api/user/tokens")
            .RequireAuthorization();
        userTokens.MapMethods("/{tokenId}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        userTokens.MapMethods("", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        userTokens.MapMethods("", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
