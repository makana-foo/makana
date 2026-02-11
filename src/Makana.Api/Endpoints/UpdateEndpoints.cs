using System.Security.Cryptography;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Security;
using Makana.Api.Storage;

namespace Makana.Api.Endpoints;

internal static class UpdateEndpoints
{
    public static IEndpointRouteBuilder MapUpdateEndpoints(this IEndpointRouteBuilder app, BackendStore store)
    {
        static async Task<bool> RequireInProgressUpdateAsync(BackendStore store, string updateId)
        {
            return await store.IsUpdateInProgressAsync(updateId);
        }

        static IResult UpdateNotInProgressResponse()
        {
            return Results.Conflict(new ErrorResponse(409, "update is not in progress", null));
        }

        static async Task<(string? NamespaceId, IResult? Failure)> ResolveNamespaceIdForRouteAsync(
            HttpContext context, BackendStore store, string org)
        {
            var userId = context.User.FindFirst(PulumiAuthenticationDefaults.UserIdClaim)?.Value
                ?? context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return (null, Results.Unauthorized());
            }

            var user = await store.GetUserByIdAsync(userId);
            var tokenOrgId = context.User.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;
            if (!string.IsNullOrWhiteSpace(tokenOrgId))
            {
                var tokenOrgLogin = await store.GetOrgLoginByIdAsync(tokenOrgId);
                var tokenOrgAlias = user is not null &&
                                    NamespaceRoutingHelpers.IsLoginCollision(tokenOrgLogin ?? string.Empty, user.Value.Login)
                    ? NamespaceRoutingHelpers.GetCollisionOrgAlias(tokenOrgLogin!, tokenOrgId)
                    : null;

                if (!string.IsNullOrWhiteSpace(tokenOrgAlias) &&
                    string.Equals(tokenOrgAlias, org, StringComparison.OrdinalIgnoreCase))
                {
                    return (tokenOrgId, null);
                }

                if (string.IsNullOrWhiteSpace(tokenOrgLogin))
                {
                    return (null, Results.NotFound(new ErrorResponse(404, "Organization not found", null)));
                }

                if (user is not null &&
                    string.Equals(org, user.Value.Login, StringComparison.OrdinalIgnoreCase))
                {
                    return (userId, null);
                }

                if (string.Equals(tokenOrgLogin, org, StringComparison.OrdinalIgnoreCase))
                {
                    return (tokenOrgId, null);
                }

                return (null, Results.NotFound(new ErrorResponse(404, "Organization not found", null)));
            }

            if (user is not null && string.Equals(user.Value.Login, org, StringComparison.OrdinalIgnoreCase))
            {
                return (userId, null);
            }

            if (user is null)
            {
                return (null, Results.NotFound(new ErrorResponse(404, "Organization not found", null)));
            }

            var memberOrgId = await NamespaceRoutingHelpers.ResolveMemberOrgIdFromRouteAsync(store, userId, user.Value.Login, org);
            if (!string.IsNullOrWhiteSpace(memberOrgId))
            {
                return (memberOrgId, null);
            }

            return (null, Results.NotFound(new ErrorResponse(404, "Organization not found", null)));
        }

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates", async (HttpContext context, string org, string project, string stack) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var stackIds = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (stackIds is null)
            {
                return Results.NotFound();
            }

            var updates = await store.GetHistoryAsync(stackIds.Value.StackId);
            var updateInfos = updates.Select(update =>
            {
                var config = EndpointHelpers.ExtractConfig(update.ProgramRequestJson);
                var (message, environment) = EndpointHelpers.ExtractMetadata(update.ProgramRequestJson);
                return new UpdateInfo(
                    Kind: update.Kind,
                    StartTime: EndpointHelpers.ToUnixTimeSeconds(update.CreatedAt),
                    Message: message,
                    Environment: environment,
                    Config: config,
                    Result: EndpointHelpers.NormalizeUpdateStatus(update.Status),
                    EndTime: EndpointHelpers.ToUnixTimeSeconds(update.CompletedAt),
                    Version: update.Version
                );
            }).ToArray();
            return Results.Ok(new GetHistoryResponse(updateInfos));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/latest", async (HttpContext context, string org, string project, string stack) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var config = await store.GetStackConfigByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            return Results.Ok(new LatestUpdateInfo(new UpdateInfo(Config: config)));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}", async (HttpContext context, string org, string project, string stack, int version) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var update = await store.GetUpdateByVersionAsync(ids.Value.StackId, version);
            if (update is null)
            {
                return Results.NotFound();
            }

            var config = EndpointHelpers.ExtractConfig(update.Value.ProgramRequestJson);
            var (message, environment) = EndpointHelpers.ExtractMetadata(update.Value.ProgramRequestJson);
            var info = new UpdateInfo(
                Kind: update.Value.Kind,
                StartTime: EndpointHelpers.ToUnixTimeSeconds(update.Value.CreatedAt),
                Message: message,
                Environment: environment,
                Config: config,
                Result: EndpointHelpers.NormalizeUpdateStatus(update.Value.Status),
                EndTime: EndpointHelpers.ToUnixTimeSeconds(update.Value.CompletedAt),
                Version: update.Value.Version
            );
            return Results.Ok(info);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}/contents/files", (string org, string project, string stack, int version) =>
        {
            return Results.Ok(new UpdateContentsFilesResponse(Array.Empty<string>()));
        });

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}/contents/file/{*path}", () =>
        {
            return Results.NotFound();
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await EndpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                store, namespaceResolution.NamespaceId!, project, stack, "update", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/preview", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await EndpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                store, namespaceResolution.NamespaceId!, project, stack, "preview", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/refresh", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await EndpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                store, namespaceResolution.NamespaceId!, project, stack, "refresh", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/destroy", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await EndpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                store, namespaceResolution.NamespaceId!, project, stack, "destroy", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}", async (HttpContext context, string org, string project, string stack, string updateId, StartUpdateRequest request) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            if (await store.HasActiveUpdateAsync(ids.Value.StackId, updateId))
            {
                return Results.Conflict(new ErrorResponse(409, "another update is already in progress", null));
            }

            var version = await store.GetNextStackVersionAsync(ids.Value.StackId);
            var token = await store.StartUpdateAsync(
                updateId,
                version,
                JsonSerializer.Serialize(request, UpdateEndpointsJsonContext.Default.StartUpdateRequest));
            var journalVersion = request.JournalVersion ?? 1;
            return Results.Ok(new StartUpdateResponse(version, token, journalVersion));
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}", async (HttpContext context, string org, string project, string stack, string updateId, StartUpdateRequest request) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, store, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            if (await store.HasActiveUpdateAsync(ids.Value.StackId, updateId))
            {
                return Results.Conflict(new ErrorResponse(409, "another update is already in progress", null));
            }

            var version = await store.GetNextStackVersionAsync(ids.Value.StackId);
            var token = await store.StartUpdateAsync(
                updateId,
                version,
                JsonSerializer.Serialize(request, UpdateEndpointsJsonContext.Default.StartUpdateRequest));
            var journalVersion = request.JournalVersion ?? 1;
            return Results.Ok(new StartUpdateResponse(version, token, journalVersion));
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/update/{updateId}", async (string updateId) =>
        {
            var update = await store.GetUpdateAsync(updateId);
            if (update is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new UpdateResults(EndpointHelpers.NormalizeUpdateStatus(update.Value.Status), update.Value.Version));
        });
        app.MapGet("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}", async (string updateId) =>
        {
            var update = await store.GetUpdateAsync(updateId);
            if (update is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new UpdateResults(EndpointHelpers.NormalizeUpdateStatus(update.Value.Status), update.Value.Version));
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/renew_lease", async (HttpContext context, string updateId, RenewUpdateLeaseRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            return Results.Ok(new RenewUpdateLeaseResponse(EndpointHelpers.GetUpdateToken(context) ?? string.Empty));
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/renew_lease", async (HttpContext context, string updateId, RenewUpdateLeaseRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            return Results.Ok(new RenewUpdateLeaseResponse(EndpointHelpers.GetUpdateToken(context) ?? string.Empty));
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/checkpoint", async (string updateId, PatchUpdateCheckpointRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await store.SaveCheckpointAsync(updateId, request.Deployment.GetRawText(), request.Version);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/checkpoint", async (string updateId, PatchUpdateCheckpointRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await store.SaveCheckpointAsync(updateId, request.Deployment.GetRawText(), request.Version);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/checkpointverbatim", async (string updateId, PatchUpdateCheckpointVerbatimRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await store.SaveCheckpointAsync(updateId, request.UntypedDeployment.GetRawText(), request.Version);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/checkpointverbatim", async (string updateId, PatchUpdateCheckpointVerbatimRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await store.SaveCheckpointAsync(updateId, request.UntypedDeployment.GetRawText(), request.Version);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/checkpointdelta", async (string updateId, PatchUpdateCheckpointDeltaRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var updateContext = await store.GetUpdateContextAsync(updateId);
            if (updateContext is null)
            {
                return Results.NotFound();
            }

            var updateCheckpoint = await store.GetLatestCheckpointForUpdateAsync(updateId);
            var baselineDeployment = updateCheckpoint?.DeploymentJson;
            if (baselineDeployment is null)
            {
                var latestDeployment = await store.GetLatestDeploymentAsync(updateContext.Value.StackId);
                baselineDeployment = latestDeployment?.DeploymentJson;
            }

            if (baselineDeployment is null)
            {
                return Results.BadRequest(new ErrorResponse(400, "no baseline deployment available for delta", null));
            }

            if (!EndpointHelpers.TryApplyTextEdits(baselineDeployment, request.DeploymentDelta, out var updatedDeployment))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid deployment delta", null));
            }

            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(updatedDeployment))).ToLowerInvariant();
            if (!string.Equals(hash, request.CheckpointHash, StringComparison.Ordinal))
            {
                return Results.Conflict(new ErrorResponse(409, "checkpoint hash mismatch", null));
            }

            await store.SaveCheckpointAsync(updateId, updatedDeployment, request.Version);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/checkpointdelta", async (string updateId, PatchUpdateCheckpointDeltaRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var updateContext = await store.GetUpdateContextAsync(updateId);
            if (updateContext is null)
            {
                return Results.NotFound();
            }

            var updateCheckpoint = await store.GetLatestCheckpointForUpdateAsync(updateId);
            var baselineDeployment = updateCheckpoint?.DeploymentJson;
            if (baselineDeployment is null)
            {
                var latestDeployment = await store.GetLatestDeploymentAsync(updateContext.Value.StackId);
                baselineDeployment = latestDeployment?.DeploymentJson;
            }

            if (baselineDeployment is null)
            {
                return Results.BadRequest(new ErrorResponse(400, "no baseline deployment available for delta", null));
            }

            if (!EndpointHelpers.TryApplyTextEdits(baselineDeployment, request.DeploymentDelta, out var updatedDeployment))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid deployment delta", null));
            }

            var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(updatedDeployment))).ToLowerInvariant();
            if (!string.Equals(hash, request.CheckpointHash, StringComparison.Ordinal))
            {
                return Results.Conflict(new ErrorResponse(409, "checkpoint hash mismatch", null));
            }

            await store.SaveCheckpointAsync(updateId, updatedDeployment, request.Version);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/journalentries", async (string updateId, JournalEntriesRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var entriesJson = new List<string>();
            if (request.Entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in request.Entries.EnumerateArray())
                {
                    entriesJson.Add(entry.GetRawText());
                }
            }
            else if (request.Entries.ValueKind != JsonValueKind.Undefined)
            {
                entriesJson.Add(request.Entries.GetRawText());
            }

            await store.RecordJournalEntriesAsync(updateId, entriesJson);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/journalentries", async (string updateId, JournalEntriesRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var entriesJson = new List<string>();
            if (request.Entries.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in request.Entries.EnumerateArray())
                {
                    entriesJson.Add(entry.GetRawText());
                }
            }
            else if (request.Entries.ValueKind != JsonValueKind.Undefined)
            {
                entriesJson.Add(request.Entries.GetRawText());
            }

            await store.RecordJournalEntriesAsync(updateId, entriesJson);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events/batch", async (string updateId, EngineEventBatch request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var eventsJson = new List<string>();
            if (request.Events.ValueKind == JsonValueKind.Array)
            {
                foreach (var ev in request.Events.EnumerateArray())
                {
                    eventsJson.Add(ev.GetRawText());
                }
            }
            else if (request.Events.ValueKind != JsonValueKind.Undefined)
            {
                eventsJson.Add(request.Events.GetRawText());
            }

            await store.RecordEventsAsync(updateId, eventsJson);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events/batch", async (string updateId, EngineEventBatch request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var eventsJson = new List<string>();
            if (request.Events.ValueKind == JsonValueKind.Array)
            {
                foreach (var ev in request.Events.EnumerateArray())
                {
                    eventsJson.Add(ev.GetRawText());
                }
            }
            else if (request.Events.ValueKind != JsonValueKind.Undefined)
            {
                eventsJson.Add(request.Events.GetRawText());
            }

            await store.RecordEventsAsync(updateId, eventsJson);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events", async (string updateId) =>
        {
            var events = await store.GetEventsAsync(updateId);
            var jsonEvents = events.Select(ev => JsonDocument.Parse(ev).RootElement).ToArray();
            return Results.Ok(new GetUpdateEventsResponse(jsonEvents, null));
        });
        app.MapGet("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events", async (string updateId) =>
        {
            var events = await store.GetEventsAsync(updateId);
            var jsonEvents = events.Select(ev => JsonDocument.Parse(ev).RootElement).ToArray();
            return Results.Ok(new GetUpdateEventsResponse(jsonEvents, null));
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events", async (string updateId, JsonElement request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await store.RecordEventsAsync(updateId, new[] { request.GetRawText() });
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events", async (string updateId, JsonElement request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await store.RecordEventsAsync(updateId, new[] { request.GetRawText() });
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/cancel", async (string updateId) =>
        {
            await store.CancelUpdateAsync(updateId);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/cancel", async (string updateId) =>
        {
            await store.CancelUpdateAsync(updateId);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/complete", async (string updateId, CompleteUpdateRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await store.CompleteUpdateAsync(updateId, request.Status);
            if (string.Equals(request.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var updateContext = await store.GetUpdateContextAsync(updateId);
                if (updateContext is { Kind: "destroy" })
                {
                    var latestDeployment = await store.GetLatestDeploymentAsync(updateContext.Value.StackId);
                    var emptyDeployment = EndpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                    await store.SaveCheckpointAsync(updateId, emptyDeployment, updateContext.Value.Version);
                }
            }
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/complete", async (string updateId, CompleteUpdateRequest request) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await store.CompleteUpdateAsync(updateId, request.Status);
            if (string.Equals(request.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var updateContext = await store.GetUpdateContextAsync(updateId);
                if (updateContext is { Kind: "destroy" })
                {
                    var latestDeployment = await store.GetLatestDeploymentAsync(updateContext.Value.StackId);
                    var emptyDeployment = EndpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                    await store.SaveCheckpointAsync(updateId, emptyDeployment, updateContext.Value.Version);
                }
            }
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        return app;
    }
}
