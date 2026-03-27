namespace Makana.Api.Endpoints;

internal static class WebhooksEndpoints
{
    public static IEndpointRouteBuilder MapWebhooksEndpoints(this IEndpointRouteBuilder app)
    {
        var orgHooks = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");
        orgHooks.MapMethods("/hooks/{webhookname}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgHooks.MapMethods("/hooks", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgHooks.MapMethods("/hooks/{webhookname}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgHooks.MapMethods("/hooks/{webhookname}/deliveries", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        orgHooks.MapMethods("/hooks/{webhookname}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgHooks.MapMethods("/hooks", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        orgHooks.MapMethods("/hooks/{webhookname}/ping", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        var stackHooks = app.MapGroup(StackRouteConventions.ApiStackRoute)
            .RequireAuthorization("OrgRouteAccess");
        stackHooks.MapMethods("/hooks/{webhookname}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackHooks.MapMethods("/hooks", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackHooks.MapMethods("/hooks/{webhookname}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackHooks.MapMethods("/hooks/{webhookname}/deliveries", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        stackHooks.MapMethods("/hooks/{webhookname}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackHooks.MapMethods("/hooks", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        stackHooks.MapMethods("/hooks/{webhookname}/ping", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        return app;
    }
}
