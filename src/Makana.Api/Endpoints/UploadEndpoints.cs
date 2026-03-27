using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class UploadEndpoints
{
    public static IEndpointRouteBuilder MapUploadEndpoints(this IEndpointRouteBuilder app, UploadRepository uploadRepository)
    {
        var uploads = app.MapGroup("/api/uploads")
            .RequireAuthorization("ApiToken");

        uploads.MapPut("/policypacks/{operationId}/archive", async (HttpRequest request, string operationId, EndpointHelpers endpointHelpers) =>
        {
            await endpointHelpers.SaveUploadAsync(uploadRepository, request, Path.Combine("policypacks", operationId, "archive"));
            return Results.Ok();
        });

        uploads.MapPut("/packages/{operationId}/{artifact}", async (HttpRequest request, string operationId, string artifact, EndpointHelpers endpointHelpers) =>
        {
            await endpointHelpers.SaveUploadAsync(uploadRepository, request, Path.Combine("packages", operationId, artifact));
            return Results.Ok();
        });

        uploads.MapPut("/templates/{operationId}/archive", async (HttpRequest request, string operationId, EndpointHelpers endpointHelpers) =>
        {
            await endpointHelpers.SaveUploadAsync(uploadRepository, request, Path.Combine("templates", operationId, "archive"));
            return Results.Ok();
        });

        return app;
    }
}
