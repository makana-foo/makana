namespace Makana.Api.Endpoints;

internal static class InsightsAccountsEndpoints
{
    public static IEndpointRouteBuilder MapInsightsAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/preview/insights/pulumi/accounts")
            .RequireAuthorization("ApiToken");

        accounts.MapMethods(string.Empty, ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        accounts.MapMethods("/{accountName}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        accounts.MapMethods("/{accountName}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        accounts.MapMethods("/{accountName}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
