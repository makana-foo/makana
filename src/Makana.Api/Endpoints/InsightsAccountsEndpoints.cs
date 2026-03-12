namespace Makana.Api.Endpoints;

internal static class InsightsAccountsEndpoints
{
    public static IEndpointRouteBuilder MapInsightsAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/preview/insights/pulumi/accounts", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/insights/pulumi/accounts/{accountName}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/insights/pulumi/accounts/{accountName}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/insights/pulumi/accounts/{accountName}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
