namespace Makana.Api.Endpoints;

internal static class EnvironmentsEndpoints
{
    public static IEndpointRouteBuilder MapEnvironmentsEndpoints(this IEndpointRouteBuilder app)
    {
        var environments = app.MapGroup("/api/esc/environments/{org}")
            .RequireAuthorization("OrgRouteAccess");

        environments.MapMethods("/{project}/{environment}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        environments.MapMethods("", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        environments.MapMethods("/{project}/{environment}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        environments.MapMethods("/{project}/{environment}/open/{openSessionID}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        environments.MapMethods("/{project}/{environment}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        environments.MapMethods("/{project}/{environment}/settings", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        environments.MapMethods("", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        environments.MapMethods("/{project}/{environment}/clone", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        environments.MapMethods("/{project}/{environment}/open", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        return app;
    }
}
