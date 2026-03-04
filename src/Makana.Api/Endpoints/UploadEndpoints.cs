using System.Diagnostics.CodeAnalysis;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class UploadEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPut(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapPut(String, Delegate)")]
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app, BackendStore store)
    {
        app.MapPut("/api/uploads/policypacks/{operationId}/archive", async (HttpRequest request, string operationId) =>
        {
            await EndpointHelpers.SaveUploadAsync(store, request, Path.Combine("policypacks", operationId, "archive"));
            return Results.Ok();
        });

        app.MapPut("/api/uploads/packages/{operationId}/{artifact}", async (HttpRequest request, string operationId, string artifact) =>
        {
            await EndpointHelpers.SaveUploadAsync(store, request, Path.Combine("packages", operationId, artifact));
            return Results.Ok();
        });

        app.MapPut("/api/uploads/templates/{operationId}/archive", async (HttpRequest request, string operationId) =>
        {
            await EndpointHelpers.SaveUploadAsync(store, request, Path.Combine("templates", operationId, "archive"));
            return Results.Ok();
        });

        return app;
    }
}
