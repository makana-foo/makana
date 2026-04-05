using System.Security.Cryptography;
using System.Text.Json;
using Makana.Api.Contracts;
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
        UpdateLogRepository updateLog)
    {
        static IResult InvalidStackNameResponse()
            => Results.BadRequest(new ErrorResponse(400, StackRouteConventions.InvalidStackNameMessage, null));

        var orgGroup = app.MapGroup("/api/stacks/{org}");
        orgGroup.RequireAuthorization("OrgRouteAccess");

        orgGroup.MapGet("/", async (HttpRequest request, string org, EndpointHelpers endpointHelpers) =>
        {
            var rows = stackQueries.ListStacks(org, null);
            var stackSummaries = await endpointHelpers.BuildStackSummariesAsync(updateQueries, request, rows);
            return Results.Ok(new ListStacksResponse(stackSummaries, null));
        });

        var projectGroup = orgGroup.MapGroup("/{project}");
        projectGroup.RequireAuthorization("OrgRouteAccess");

        projectGroup.MapMethods("/", ["HEAD"], (string org, string project) =>
        {
            var exists = stackQueries.ProjectExists(org, project);
            return exists ? Results.Ok() : Results.NotFound();
        });

        projectGroup.MapPost("/", (string org, string project, CreateStackRequest request) =>
        {
            if (!StackRouteConventions.IsValidStackName(request.StackName))
            {
                return InvalidStackNameResponse();
            }

            var existing = stackQueries.GetStackIds(org, project, request.StackName);
            if (existing is not null)
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
            var stackId = stackCommands.CreateStack(
                org,
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
        }).RequireAuthorization("ApiToken");

        var stackGroup = projectGroup.MapGroup(StackRouteConventions.RelativeStackRoute);
        stackGroup.RequireAuthorization("OrgRouteAccess");

        stackGroup.MapGet("/", (string org, string project, string stack) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var data = stackQueries.GetStackData(org, project, stack);

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
                project,
                stack,
                null,
                activeUpdate,
                tags,
                null,
                version);
            return Results.Ok(result);
        });

        stackGroup.MapPost("/rename", async (
            string org,
            string project,
            string stack,
            StackRenameRequest request,
            CancellationToken cancellationToken) =>
        {
            if (!StackRouteConventions.IsValidStackName(request.NewName))
            {
                return InvalidStackNameResponse();
            }

            stackCommands.RenameStack(org, project, stack, request.NewName, request.NewProject, cancellationToken);
            return Results.Ok();
        }).RequireAuthorization("ApiToken");

        stackGroup.MapDelete("/", (string org, string project, string stack, bool force = false) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
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

            stackCommands.DeleteStack(org, project, stack);
            return Results.Ok();
        }).RequireAuthorization("ApiToken");

        stackGroup.MapPatch("/tags", (string org, string project, string stack, Dictionary<string, string> tags) =>
        {
            stackCommands.UpdateTags(
                org,
                project,
                stack,
                JsonSerializer.Serialize(tags, StackEndpointsJsonContext.Default.DictionaryStringString));
            return Results.Ok();
        }).RequireAuthorization("ApiToken");

        stackGroup.MapPut("/config", (string org, string project, string stack, StackConfig config) =>
        {
            stackCommands.UpdateConfig(
                org,
                project,
                stack,
                JsonSerializer.Serialize(
                    config.Config,
                    StackEndpointsJsonContext.Default.DictionaryStringStackConfigValue));
            return Results.Ok();
        }).RequireAuthorization("ApiToken");

        stackGroup.MapGet("/logs", () =>
        {
            throw new NotImplementedException();
            return Results.Ok(new LogsResult([]));
        });

        stackGroup.MapGet("/export", async (string org, string project, string stack, EndpointHelpers endpointHelpers) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
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

        stackGroup.MapGet("/export/{version}", async (string org, string project, string stack, int version) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
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

        stackGroup.MapPost("/import", async (string org, string project, string stack, UntypedDeployment deployment, ImportUpdateEventFactory importUpdateEventFactory, CancellationToken cancellationToken) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
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
                project,
                stack);
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
        }).RequireAuthorization("ApiToken");

        stackGroup.MapPost("/encrypt",
            (EncryptValueRequest request, EndpointHelpers endpointHelpers, IdentityRepository identityRepository) =>
            {
                var secretKey = identityRepository.GetOrCreateSecretKey();
                return Results.Ok(new EncryptValueResponse(endpointHelpers.Encrypt(secretKey, request.Plaintext)));
            })
            .RequireAuthorization("ApiToken");

        stackGroup.MapPost("/decrypt",
            (DecryptValueRequest request, EndpointHelpers endpointHelpers, IdentityRepository identityRepository) =>
            {
                var secretKey = identityRepository.GetOrCreateSecretKey();
                return Results.Ok(new DecryptValueResponse(endpointHelpers.Decrypt(secretKey, request.Ciphertext)));
            })
            .RequireAuthorization("ApiToken");

        stackGroup.MapPost("/decrypt/log-decryption",
            (LogDecryptionEventRequest request) => Results.Ok())
            .RequireAuthorization("ApiToken");

        stackGroup.MapPost("/decrypt/log-batch-decryption",
            (LogDecryptionEventRequest request) => Results.Ok())
            .RequireAuthorization("ApiToken");

        stackGroup.MapPost("/batch-encrypt",
            (BatchEncryptRequest request, EndpointHelpers endpointHelpers, IdentityRepository identityRepository) =>
            {
                var secretKey = identityRepository.GetOrCreateSecretKey();
                var cipher = request.Plaintexts.Select(p => endpointHelpers.Encrypt(secretKey, p)).ToArray();
                return Results.Ok(new BatchEncryptResponse(cipher));
            }).RequireAuthorization("ApiToken");

        stackGroup.MapPost("/batch-decrypt",
            (JsonElement request, EndpointHelpers endpointHelpers, IdentityRepository identityRepository) =>
            {
                var secretKey = identityRepository.GetOrCreateSecretKey();
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
            }).RequireAuthorization("ApiToken");

        return app;
    }
}
