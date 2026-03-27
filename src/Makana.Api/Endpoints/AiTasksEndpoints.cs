namespace Makana.Api.Endpoints;

internal static class AiTasksEndpoints
{
    public static IEndpointRouteBuilder MapAiTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var tasks = app.MapGroup("/api/preview/agents/{org}")
            .RequireAuthorization("ApiToken", "OrgRouteAccess");

        tasks.MapMethods("/tasks", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        tasks.MapMethods("/tasks/{taskID}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        tasks.MapMethods("/tasks/{taskID}/events", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        tasks.MapMethods("/tasks", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        tasks.MapMethods("/tasks/{taskID}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
