namespace Makana.Api.Endpoints;

internal static class ServicesEndpoints
{
    public static IEndpointRouteBuilder MapServicesEndpoints(this IEndpointRouteBuilder app)
    {
        var servicesGroup = app.MapGroup("/api/orgs/{org}")
            .RequireAuthorization("OrgRouteAccess");

        servicesGroup.MapMethods("/services/{ownerType}/{ownerName}/{serviceName}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        servicesGroup.MapMethods("/services/{ownerType}/{ownerName}/{serviceName}/items/{itemType}/{itemName}", ["DELETE"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        servicesGroup.MapMethods("/services", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        servicesGroup.MapMethods("/services/{ownerType}/{ownerName}/{serviceName}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        servicesGroup.MapMethods("/services/{ownerType}/{ownerName}/{serviceName}", ["PATCH"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        servicesGroup.MapMethods("/services", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");
        servicesGroup.MapMethods("/services/{ownerType}/{ownerName}/{serviceName}/items", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."))
            .RequireAuthorization("ApiToken");

        return app;
    }
}
