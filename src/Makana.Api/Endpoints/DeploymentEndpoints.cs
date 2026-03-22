using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class DeploymentEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapDeploymentEndpoints(this IEndpointRouteBuilder app, BackendStore store, byte[] secretKey)
    {
        app.MapGet("/api/stacks/{org}/{project}/{stack}/deployments/settings", async (string org, string project, string stack) =>
        {
            var ids = await store.GetStackIdsAsync(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var settings = await store.GetDeploymentSettingsAsync(ids.Value.StackId);
            if (string.IsNullOrWhiteSpace(settings))
            {
                return Results.Ok(new DeploymentSettingsRequest(null, null, null, null, null, null));
            }

            return Results.Ok(JsonDocument.Parse(settings).RootElement);
        });

        app.MapPut("/api/stacks/{org}/{project}/{stack}/deployments/settings", async (string org, string project, string stack, JsonElement settings) =>
        {
            var ids = await store.GetStackIdsAsync(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            await store.SaveDeploymentSettingsAsync(ids.Value.StackId, settings.GetRawText());
            return Results.Ok();
        });

        app.MapDelete("/api/stacks/{org}/{project}/{stack}/deployments/settings", async (string org, string project, string stack) =>
        {
            var ids = await store.GetStackIdsAsync(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            await store.DeleteDeploymentSettingsAsync(ids.Value.StackId);
            return Results.Ok();
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/deployments/settings/encrypt", (JsonElement request, EndpointHelpers endpointHelpers) =>
        {
            var plaintext = endpointHelpers.ExtractSecretPlaintext(request);
            var cipher = endpointHelpers.Encrypt(secretKey, System.Text.Encoding.UTF8.GetBytes(plaintext));
            return Results.Ok(new SecretValueResponse(Convert.ToBase64String(cipher)));
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/deployments", async (HttpContext context, string org, string project, string stack, CreateDeploymentRequest request, EndpointHelpers endpointHelpers) =>
        {
            var ids = await store.GetStackIdsAsync(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var deploymentId = await store.CreateDeploymentRunAsync(
                ids.Value.StackId,
                JsonSerializer.Serialize(request, DeploymentEndpointsJsonContext.Default.CreateDeploymentRequest));
            var baseUrl = endpointHelpers.GetBaseUrl(context.Request);
            return Results.Ok(new CreateDeploymentResponse(deploymentId, $"{baseUrl}/console/deployments/{deploymentId}"));
        }).RequireAuthorization("ApiToken");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/deployments/{deploymentId}/logs", async (string deploymentId) =>
        {
            if (!await store.DeploymentRunExistsAsync(deploymentId))
            {
                return Results.NotFound();
            }

            return Results.Ok(new DeploymentLogs(Array.Empty<DeploymentLogLine>(), null));
        });

        app.MapGet("/api/stacks/{org}/{project}/{stack}/deployments/{deploymentId}/updates", async (string deploymentId) =>
        {
            if (!await store.DeploymentRunExistsAsync(deploymentId))
            {
                return Results.NotFound();
            }

            return Results.Ok(Array.Empty<object>());
        });

        return app;
    }
}
