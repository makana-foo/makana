using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class PersonalAccessTokensEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapPersonalAccessTokensEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/orgs/{org}/tokens/{tokenId}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/user/tokens/{tokenId}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{organization}/tokens", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/user/tokens", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/orgs/{org}/tokens", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/user/tokens", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
