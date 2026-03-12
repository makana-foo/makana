using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class DeploymentEndpoints
{
    public static IEndpointRouteBuilder MapDeploymentEndpoints(
        this IEndpointRouteBuilder app,
        StackCatalogQueryRepository stackQueries,
        DeploymentRunRepository deploymentRuns,
        byte[] secretKey)
    {
        app.MapGet("/api/stacks/{org}/{project}/{stack}/deployments/settings", async (string org, string project, string stack) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var settings = deploymentRuns.GetDeploymentSettings(ids.Value.StackId);
            if (string.IsNullOrWhiteSpace(settings))
            {
                return Results.Ok(new DeploymentSettingsRequest(null, null, null, null, null, null));
            }

            return Results.Ok(JsonDocument.Parse(settings).RootElement);
        });

        app.MapPut("/api/stacks/{org}/{project}/{stack}/deployments/settings", async (string org, string project, string stack, JsonElement settings) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            deploymentRuns.SaveDeploymentSettings(ids.Value.StackId, settings.GetRawText());
            return Results.Ok();
        });

        app.MapDelete("/api/stacks/{org}/{project}/{stack}/deployments/settings", async (string org, string project, string stack) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            deploymentRuns.DeleteDeploymentSettings(ids.Value.StackId);
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
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var deploymentId = deploymentRuns.CreateDeploymentRun(
                ids.Value.StackId,
                JsonSerializer.Serialize(request, DeploymentEndpointsJsonContext.Default.CreateDeploymentRequest));
            var baseUrl = endpointHelpers.GetBaseUrl(context.Request);
            return Results.Ok(new CreateDeploymentResponse(deploymentId, $"{baseUrl}/console/deployments/{deploymentId}"));
        }).RequireAuthorization("ApiToken");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/deployments/{deploymentId}/logs", async (string deploymentId) =>
        {
            if (!deploymentRuns.DeploymentRunExists(deploymentId))
            {
                return Results.NotFound();
            }

            return Results.Ok(new DeploymentLogs([], null));
        });

        app.MapGet("/api/stacks/{org}/{project}/{stack}/deployments/{deploymentId}/updates", async (string deploymentId) =>
        {
            if (!deploymentRuns.DeploymentRunExists(deploymentId))
            {
                return Results.NotFound();
            }

            return Results.Ok(Array.Empty<object>());
        });

        return app;
    }
}
