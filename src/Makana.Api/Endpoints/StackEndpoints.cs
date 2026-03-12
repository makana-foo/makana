using System.Security.Cryptography;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;
using Makana.Api.Updates;
using Makana.Journaling.Checkpointing;
using Makana.StackUpdates.Pulumi;

namespace Makana.Api.Endpoints;

internal static class StackEndpoints
{
    public static IEndpointRouteBuilder MapStackEndpoints(
        this IEndpointRouteBuilder app,
        StackCatalogQueryRepository stackQueries,
        StackCatalogCommandRepository stackCommands,
        UpdateCommandRepository updateCommands,
        UpdateQueryRepository updateQueries,
        UpdateLogRepository updateLog,
        byte[] secretKey)
    {
        var stacksGroup = app.MapGroup("/api/stacks/{org}");
        stacksGroup.RequireAuthorization("OrgRouteAccess");

        stacksGroup.MapGet("", async (HttpContext context, HttpRequest request, string org, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var rows = stackQueries.ListStacksByNamespaceIds(null, [namespaceResolution.NamespaceId!]);
            var stackSummaries = await endpointHelpers.BuildStackSummariesAsync(updateQueries, request, rows);
            return Results.Ok(new ListStacksResponse(stackSummaries, null));
        });

        stacksGroup.MapMethods("/{project}", ["HEAD"], async (HttpContext context, string org, string project, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var exists = stackQueries.ProjectExistsByNamespaceId(namespaceResolution.NamespaceId!, project);
            return exists ? Results.Ok() : Results.NotFound();
        });

        stacksGroup.MapGet("/{project}/{stack}", async (HttpContext context, string org, string project, string stack, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var decodedProject = Uri.UnescapeDataString(project);
            var decodedStack = Uri.UnescapeDataString(stack);
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, decodedProject, decodedStack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var data = stackQueries.GetStackDataByNamespaceId(namespaceResolution.NamespaceId!, decodedProject, decodedStack);

            Dictionary<string, string>? tags = null;
            if (data?.TagsJson is not null)
            {
                tags = JsonSerializer.Deserialize(
                    data.Value.TagsJson,
                    StackEndpointsJsonContext.Default.DictionaryStringString);
            }

            var activeUpdate = stackQueries.GetActiveUpdateId(ids.Value.StackId) ?? string.Empty;
            var deployment = updateQueries.GetLatestDeployment(ids.Value.StackId);
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
        });

        stacksGroup.MapPost("/{project}", async (HttpContext context, string org, string project, CreateStackRequest request, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var existing = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, request.StackName);
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
            var stackId = stackCommands.CreateStackByNamespaceId(
                namespaceResolution.NamespaceId!,
                project,
                request.StackName,
                tagsJson,
                configJson);

            if (request.State is { ValueKind: JsonValueKind.Object } stateElement)
            {
                UntypedDeployment? initialState;
                try
                {
                    initialState = stateElement.Deserialize(StackEndpointsJsonContext.Default.UntypedDeployment);
                }
                catch (JsonException)
                {
                    return Results.BadRequest(new ErrorResponse(400, "state is invalid", null));
                }

                if (initialState is null)
                {
                    return Results.BadRequest(new ErrorResponse(400, "state is invalid", null));
                }

                int initialSchemaVersion;
                try
                {
                    initialSchemaVersion = DeploymentSchemaPolicy.ResolvePersistedSchemaVersion(initialState);
                }
                catch (InvalidOperationException)
                {
                    return Results.BadRequest(new ErrorResponse(400, "state is invalid", null));
                }

                var initialVersion = updateCommands.GetNextStackVersion(stackId);
                stackCommands.SaveInitialDeploymentForStack(
                    stackId,
                    initialVersion,
                    initialSchemaVersion,
                    initialState.Deployment.GetRawText());
            }

            return Results.Ok();
        });

        stacksGroup.MapPost("/{project}/{stack}/rename", async (
            string org,
            string project,
            string stack,
            StackRenameRequest request,
            CancellationToken cancellationToken) =>
        {
            var (decodedProject, decodedStack) = DecodeStackRoute(project, stack);
            stackCommands.RenameStack(org, decodedProject, decodedStack, request.NewName, request.NewProject, cancellationToken);
            return Results.Ok();
        });

        stacksGroup.MapDelete("/{project}/{stack}", async (string org, string project, string stack, bool force = false) =>
        {
            var (decodedProject, decodedStack) = DecodeStackRoute(project, stack);
            var ids = stackQueries.GetStackIds(org, decodedProject, decodedStack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            if (!force)
            {
                var deployment = updateQueries.GetLatestDeployment(ids.Value.StackId);
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

            stackCommands.DeleteStack(org, decodedProject, decodedStack);
            return Results.Ok();
        });

        stacksGroup.MapPatch("/{project}/{stack}/tags", async (string org, string project, string stack, Dictionary<string, string> tags) =>
        {
            var (decodedProject, decodedStack) = DecodeStackRoute(project, stack);
            stackCommands.UpdateTags(
                org,
                decodedProject,
                decodedStack,
                JsonSerializer.Serialize(tags, StackEndpointsJsonContext.Default.DictionaryStringString));
            return Results.Ok();
        });

        stacksGroup.MapPut("/{project}/{stack}/config", async (string org, string project, string stack, StackConfig config) =>
        {
            var (decodedProject, decodedStack) = DecodeStackRoute(project, stack);
            stackCommands.UpdateConfig(
                org,
                decodedProject,
                decodedStack,
                JsonSerializer.Serialize(
                    config.Config,
                    StackEndpointsJsonContext.Default.DictionaryStringStackConfigValue));
            return Results.Ok();
        });

        stacksGroup.MapGet("/{project}/{stack}/logs", () =>
        {
            return Results.Ok(new LogsResult([]));
        });

        stacksGroup.MapGet("/{project}/{stack}/export", async (string org, string project, string stack, EndpointHelpers endpointHelpers) =>
        {
            var (decodedProject, decodedStack) = DecodeStackRoute(project, stack);
            var ids = stackQueries.GetStackIds(org, decodedProject, decodedStack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var deployment = updateQueries.GetLatestDeploymentEnvelope(ids.Value.StackId);
            if (deployment is null)
            {
                var emptyDeployment = JsonDocument.Parse(endpointHelpers.CreateEmptyDeploymentJson()).RootElement;
                return Results.Ok(new ExportStackResponse(
                    DeploymentSchemaPolicy.OldestSupportedVersion,
                    null,
                    emptyDeployment));
            }

            var payload = new ExportStackResponse(
                deployment.Value.SchemaVersion,
                DeploymentSchemaPolicy.DescribeFeaturesForExport(
                    deployment.Value.DeploymentJson,
                    deployment.Value.SchemaVersion),
                JsonDocument.Parse(deployment.Value.DeploymentJson).RootElement);
            return Results.Ok(payload);
        });

        stacksGroup.MapGet("/{project}/{stack}/export/{version}", async (string org, string project, string stack, int version) =>
        {
            var (decodedProject, decodedStack) = DecodeStackRoute(project, stack);
            var ids = stackQueries.GetStackIds(org, decodedProject, decodedStack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var deployment = updateQueries.GetDeploymentEnvelopeByVersion(ids.Value.StackId, version);
            if (deployment is null)
            {
                return Results.NotFound();
            }

            var payload = new ExportStackResponse(
                deployment.Value.SchemaVersion,
                DeploymentSchemaPolicy.DescribeFeaturesForExport(
                    deployment.Value.DeploymentJson,
                    deployment.Value.SchemaVersion),
                JsonDocument.Parse(deployment.Value.DeploymentJson).RootElement);
            return Results.Ok(payload);
        });

        stacksGroup.MapPost("/{project}/{stack}/import", async (string org, string project, string stack, UntypedDeployment deployment, ImportUpdateEventFactory importUpdateEventFactory, CancellationToken cancellationToken) =>
        {
            var (decodedProject, decodedStack) = DecodeStackRoute(project, stack);
            var ids = stackQueries.GetStackIds(org, decodedProject, decodedStack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            int persistedSchemaVersion;
            try
            {
                persistedSchemaVersion = DeploymentSchemaPolicy.ResolvePersistedSchemaVersion(deployment);
            }
            catch (DeploymentSchemaTooNewException)
            {
                return Results.BadRequest(new ErrorResponse(
                    400,
                    $"the stack '{stack}' is newer than what this version of the Pulumi CLI understands",
                    null));
            }
            catch (DeploymentSchemaTooOldException)
            {
                return Results.BadRequest(new ErrorResponse(
                    400,
                    $"the stack '{stack}' is too old",
                    null));
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new ErrorResponse(400, ex.Message, null));
            }

            var rewrittenDeploymentJson = stackCommands.RewriteImportedDeploymentForTargetStack(
                deployment.Deployment.GetRawText(),
                org,
                decodedProject,
                decodedStack);
            using var rewrittenDeployment = JsonDocument.Parse(rewrittenDeploymentJson);

            var updateId = updateCommands.CreateUpdate(ids.Value.StackId, "import", rewrittenDeploymentJson);
            var version = updateCommands.GetNextStackVersion(ids.Value.StackId);
            updateLog.SaveCheckpoint(updateId, rewrittenDeploymentJson, version, persistedSchemaVersion);
            var events = importUpdateEventFactory.CreateEvents(rewrittenDeployment.RootElement);
            updateLog.RecordEvents(
                updateId,
                events.Select(static ev => JsonSerializer.Serialize(ev, PulumiEngineEventJsonContext.Default.EngineEvent)));
            updateCommands.CompleteUpdate(updateId, "succeeded", version);
            return Results.Ok(new ImportStackResponse(updateId));
        });

        stacksGroup.MapPost("/{project}/{stack}/encrypt",
            (EncryptValueRequest request, EndpointHelpers endpointHelpers) => Results.Ok(new EncryptValueResponse(endpointHelpers.Encrypt(secretKey, request.Plaintext))));

        stacksGroup.MapPost("/{project}/{stack}/decrypt",
            (DecryptValueRequest request, EndpointHelpers endpointHelpers) => Results.Ok(new DecryptValueResponse(endpointHelpers.Decrypt(secretKey, request.Ciphertext))));

        stacksGroup.MapPost("/{project}/{stack}/decrypt/log-decryption",
            (LogDecryptionEventRequest request) => Results.Ok());

        stacksGroup.MapPost("/{project}/{stack}/decrypt/log-batch-decryption",
            (LogDecryptionEventRequest request) => Results.Ok());

        stacksGroup.MapPost("/{project}/{stack}/batch-encrypt",
            (BatchEncryptRequest request, EndpointHelpers endpointHelpers) =>
            {
                var cipher = request.Plaintexts.Select(p => endpointHelpers.Encrypt(secretKey, p)).ToArray();
                return Results.Ok(new BatchEncryptResponse(cipher));
            });

        stacksGroup.MapPost("/{project}/{stack}/batch-decrypt",
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
            });

        return app;
    }

    private static (string Project, string Stack) DecodeStackRoute(string project, string stack)
        => (Uri.UnescapeDataString(project), Uri.UnescapeDataString(stack));

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
