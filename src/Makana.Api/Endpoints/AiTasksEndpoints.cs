using System.Diagnostics.CodeAnalysis;

namespace Makana.Api.Endpoints;

internal static class AiTasksEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapAiTasksEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/preview/agents/{orgName}/tasks", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/agents/{orgName}/tasks/{taskID}", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/agents/{orgName}/tasks/{taskID}/events", ["GET"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/agents/{orgName}/tasks", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));
        app.MapMethods("/api/preview/agents/{orgName}/tasks/{taskID}", ["POST"], static (HttpContext _) => throw new NotImplementedException("Endpoint not implemented."));

        return app;
    }
}
