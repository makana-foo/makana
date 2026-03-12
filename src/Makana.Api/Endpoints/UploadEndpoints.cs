using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app, UploadRepository uploadRepository)
    {
        app.MapPut("/api/uploads/policypacks/{operationId}/archive", async (HttpRequest request, string operationId, EndpointHelpers endpointHelpers) =>
        {
            await endpointHelpers.SaveUploadAsync(uploadRepository, request, Path.Combine("policypacks", operationId, "archive"));
            return Results.Ok();
        });

        app.MapPut("/api/uploads/packages/{operationId}/{artifact}", async (HttpRequest request, string operationId, string artifact, EndpointHelpers endpointHelpers) =>
        {
            await endpointHelpers.SaveUploadAsync(uploadRepository, request, Path.Combine("packages", operationId, artifact));
            return Results.Ok();
        });

        app.MapPut("/api/uploads/templates/{operationId}/archive", async (HttpRequest request, string operationId, EndpointHelpers endpointHelpers) =>
        {
            await endpointHelpers.SaveUploadAsync(uploadRepository, request, Path.Combine("templates", operationId, "archive"));
            return Results.Ok();
        });

        return app;
    }
}
