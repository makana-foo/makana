using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;
using Makana.Api.Updates;
using Makana.StackUpdates.Pulumi;

namespace Makana.Api.Endpoints;

internal static class StackEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapStackEndpoints(this IEndpointRouteBuilder app, BackendStore store, byte[] secretKey)
    {
        app.MapGet("/api/stacks/{org}", async (HttpContext context, HttpRequest request, string org, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var rows = await store.ListStacksByNamespaceIdsAsync(null, new[] { namespaceResolution.NamespaceId! });
            var stacks = await endpointHelpers.BuildStackSummariesAsync(store, request, rows);
            return Results.Ok(new ListStacksResponse(stacks, null));
        }).RequireAuthorization();

        app.MapMethods("/api/stacks/{org}/{project}", new[] { "HEAD" }, async (HttpContext context, string org, string project, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var exists = await store.ProjectExistsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project);
            return exists ? Results.Ok() : Results.NotFound();
        }).RequireAuthorization();

        app.MapGet("/api/stacks/{org}/{project}/{stack}", async (HttpContext context, string org, string project, string stack, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var decodedProject = Uri.UnescapeDataString(project);
            var decodedStack = Uri.UnescapeDataString(stack);
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, decodedProject, decodedStack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var data = await store.GetStackDataByNamespaceIdAsync(namespaceResolution.NamespaceId!, decodedProject, decodedStack);

            Dictionary<string, string>? tags = null;
            if (data?.TagsJson is not null)
            {
                tags = JsonSerializer.Deserialize(
                    data.Value.TagsJson,
                    StackEndpointsJsonContext.Default.DictionaryStringString);
            }

            var activeUpdate = await store.GetActiveUpdateIdAsync(ids.Value.StackId) ?? string.Empty;
            var deployment = await store.GetLatestDeploymentAsync(ids.Value.StackId);
            var version = deployment?.Version ?? 0;

            var result = new Stack(
                ids.Value.StackId,
                org,
                decodedProject,
                decodedStack,
                null,
                activeUpdate,
                tags,
                null,
                version);
            return Results.Ok(result);
        }).RequireAuthorization();

        app.MapPost("/api/stacks/{org}/{project}", async (HttpContext context, string org, string project, CreateStackRequest request, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var existing = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, request.StackName);
            if (existing != null)
            {
                return Results.Conflict(new ErrorResponse(409, "stack already exists", null));
            }

            var tagsJson = request.Tags is null
                ? null
                : JsonSerializer.Serialize(request.Tags, StackEndpointsJsonContext.Default.DictionaryStringString);
            var configJson = request.Config is null
                ? null
                : JsonSerializer.Serialize(
                    request.Config.Config,
                    StackEndpointsJsonContext.Default.DictionaryStringStackConfigValue);
            await store.CreateStackByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, request.StackName, tagsJson, configJson);

            return Results.Ok();
        }).RequireAuthorization();

        app.MapPost("/api/stacks/{org}/{project}/{stack}/rename", async (
            string org,
            string project,
            string stack,
            StackRenameRequest request,
            CancellationToken cancellationToken) =>
        {
            await store.RenameStackAsync(org, project, stack, request.NewName, request.NewProject, cancellationToken);
            return Results.Ok();
        }).RequireAuthorization("OrgRouteAccess");

        app.MapDelete("/api/stacks/{org}/{project}/{stack}", async (string org, string project, string stack, bool force) =>
        {
            var ids = await store.GetStackIdsAsync(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            if (!force)
            {
                var deployment = await store.GetLatestDeploymentAsync(ids.Value.StackId);
                if (deployment is not null)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(deployment.Value.DeploymentJson);
                        if (doc.RootElement.TryGetProperty("resources", out var resources) &&
                            resources.ValueKind == JsonValueKind.Array)
                        {
                            var hasManaged = resources.EnumerateArray().Any(resource =>
                                resource.TryGetProperty("type", out var type) &&
                                type.ValueKind == JsonValueKind.String &&
                                !string.Equals(type.GetString(), "pulumi:pulumi:Stack", StringComparison.Ordinal));

                            if (hasManaged)
                            {
                                return Results.Conflict(new ErrorResponse(
                                    409,
                                    "stack has resources; run pulumi destroy or pass force=true",
                                    null));
                            }
                        }
                    }
                    catch
                    {
                        return Results.Conflict(new ErrorResponse(
                            409,
                            "stack state unreadable; pass force=true to remove",
                            null));
                    }
                }
            }

            await store.DeleteStackAsync(org, project, stack);
            return Results.Ok();
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/tags", async (string org, string project, string stack, Dictionary<string, string> tags) =>
        {
            await store.UpdateTagsAsync(
                org,
                project,
                stack,
                JsonSerializer.Serialize(tags, StackEndpointsJsonContext.Default.DictionaryStringString));
            return Results.Ok();
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPut("/api/stacks/{org}/{project}/{stack}/config", async (string org, string project, string stack, StackConfig config) =>
        {
            await store.UpdateConfigAsync(
                org,
                project,
                stack,
                JsonSerializer.Serialize(
                    config.Config,
                    StackEndpointsJsonContext.Default.DictionaryStringStackConfigValue));
            return Results.Ok();
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/logs", () =>
        {
            return Results.Ok(new LogsResult(Array.Empty<LogEntry>()));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/export", async (string org, string project, string stack, EndpointHelpers endpointHelpers) =>
        {
            var ids = await store.GetStackIdsAsync(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var deployment = await store.GetLatestDeploymentAsync(ids.Value.StackId);
            if (deployment is null)
            {
                var emptyDeployment = JsonDocument.Parse(endpointHelpers.CreateEmptyDeploymentJson()).RootElement;
                return Results.Ok(new ExportStackResponse(3, null, emptyDeployment));
            }

            var payload = new ExportStackResponse(
                3,
                null,
                JsonDocument.Parse(deployment.Value.DeploymentJson).RootElement);
            return Results.Ok(payload);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/export/{version}", async (string org, string project, string stack, int version) =>
        {
            var ids = await store.GetStackIdsAsync(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var deployment = await store.GetLatestDeploymentAsync(ids.Value.StackId);
            if (deployment is null || deployment.Value.Version != version)
            {
                return Results.NotFound();
            }

            var payload = new ExportStackResponse(
                3,
                null,
                JsonDocument.Parse(deployment.Value.DeploymentJson).RootElement);
            return Results.Ok(payload);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/import", async (string org, string project, string stack, UntypedDeployment deployment, ImportUpdateEventFactory importUpdateEventFactory, CancellationToken cancellationToken) =>
        {
            var ids = await store.GetStackIdsAsync(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var updateId = await store.CreateUpdateAsync(ids.Value.StackId, "import", deployment.Deployment.GetRawText());
            var version = await store.GetNextStackVersionAsync(ids.Value.StackId);
            await store.SaveCheckpointAsync(updateId, deployment.Deployment.GetRawText(), version, cancellationToken);
            var events = importUpdateEventFactory.CreateEvents(deployment.Deployment);
            await store.RecordEventsAsync(
                updateId,
                events.Select(static ev => JsonSerializer.Serialize(ev, PulumiEngineEventJsonContext.Default.EngineEvent)),
                cancellationToken);
            await store.CompleteUpdateAsync(updateId, "succeeded", version, cancellationToken);
            return Results.Ok(new ImportStackResponse(updateId));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/encrypt",
            (EncryptValueRequest request, EndpointHelpers endpointHelpers) => Results.Ok(new EncryptValueResponse(endpointHelpers.Encrypt(secretKey, request.Plaintext))))
            .RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/decrypt",
            (DecryptValueRequest request, EndpointHelpers endpointHelpers) => Results.Ok(new DecryptValueResponse(endpointHelpers.Decrypt(secretKey, request.Ciphertext))))
            .RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/decrypt/log-decryption",
            (LogDecryptionEventRequest request) => Results.Ok())
            .RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/decrypt/log-batch-decryption",
            (LogDecryptionEventRequest request) => Results.Ok())
            .RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/batch-encrypt",
            (BatchEncryptRequest request, EndpointHelpers endpointHelpers) =>
            {
                var cipher = request.Plaintexts.Select(p => endpointHelpers.Encrypt(secretKey, p)).ToArray();
                return Results.Ok(new BatchEncryptResponse(cipher));
            })
            .RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/batch-decrypt",
            (JsonElement request, EndpointHelpers endpointHelpers) =>
            {
                if (!request.TryGetProperty("ciphertexts", out var ciphertextsElement) ||
                    ciphertextsElement.ValueKind != JsonValueKind.Array)
                {
                    return Results.BadRequest(new ErrorResponse(400, "ciphertexts is required", null));
                }

                var plaintexts = new Dictionary<string, byte[]>();
                foreach (var cipherElement in ciphertextsElement.EnumerateArray())
                {
                    if (cipherElement.ValueKind != JsonValueKind.String)
                    {
                        return Results.BadRequest(new ErrorResponse(400, "ciphertexts must be base64 strings", null));
                    }

                    var cipherText = cipherElement.GetString() ?? string.Empty;
                    byte[] cipherBytes;
                    try
                    {
                        cipherBytes = Convert.FromBase64String(cipherText);
                    }
                    catch (FormatException)
                    {
                        return Results.BadRequest(new ErrorResponse(400, "ciphertexts must be base64 strings", null));
                    }

                    try
                    {
                        plaintexts[cipherText] = endpointHelpers.Decrypt(secretKey, cipherBytes);
                    }
                    catch (CryptographicException)
                    {
                        return Results.BadRequest(new ErrorResponse(400, "invalid ciphertext", null));
                    }
                }

                return Results.Ok(new BatchDecryptResponse(plaintexts));
            })
            .RequireAuthorization("OrgRouteAccess");

        return app;
    }

    private static async Task<(string? NamespaceId, IResult? Failure)> ResolveNamespaceIdForRouteAsync(
        HttpContext context,
        NamespaceRoutingHelpers namespaceRoutingHelpers,
        string org)
    {
        var userId = context.User.FindFirst(PulumiAuthenticationDefaults.UserIdClaim)?.Value
            ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userId))
        {
            return (null, Results.Unauthorized());
        }

        var tokenOrgId = context.User.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;
        var namespaceId = await namespaceRoutingHelpers.ResolveAccessibleNamespaceIdAsync(
            userId,
            org,
            tokenOrgId);
        if (!string.IsNullOrWhiteSpace(namespaceId))
        {
            return (namespaceId, null);
        }

        return (null, Results.NotFound(new ErrorResponse(404, "Organization not found", null)));
    }
}
