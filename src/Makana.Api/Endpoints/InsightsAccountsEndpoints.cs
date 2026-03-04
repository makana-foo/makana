using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class InsightsAccountsEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapInsightsAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/preview/insights/pulumi/accounts", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/insights/pulumi/accounts/{accountName}", new[] { "GET" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/insights/pulumi/accounts/{accountName}", new[] { "PATCH" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/insights/pulumi/accounts/{accountName}", new[] { "POST" }, static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
