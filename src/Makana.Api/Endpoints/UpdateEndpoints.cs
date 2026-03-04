using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Realtime;
using Makana.Api.Security;
using Makana.Api.Storage;
using Makana.Api.Updates;

namespace Makana.Api.Endpoints;

internal static class UpdateEndpoints
{
    [RequiresUnreferencedCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions.MapGet(String, Delegate)")]
    public static IEndpointRouteBuilder MapUpdateEndpoints(
        this IEndpointRouteBuilder app,
        BackendStore store,
        IStackUpdateNotifier updateNotifier,
        IUpdateCheckpointJournalingProvider checkpointJournalingProvider)
    {
        static async Task<bool> RequireInProgressUpdateAsync(
            BackendStore store,
            string updateId,
            CancellationToken cancellationToken)
        {
            return await store.IsUpdateInProgressAsync(updateId, cancellationToken);
        }

        static IResult UpdateNotInProgressResponse()
        {
            return Results.Conflict(new ErrorResponse(409, "update is not in progress", null));
        }

        static IResult ToMutationError(UpdateMutationResult result)
        {
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "request could not be applied"
                : result.Message;
            return result.Status switch
            {
                UpdateMutationStatus.NotFound => Results.NotFound(),
                UpdateMutationStatus.BadRequest => Results.BadRequest(new ErrorResponse(400, message, null)),
                UpdateMutationStatus.Conflict => Results.Conflict(new ErrorResponse(409, message, null)),
                _ => Results.Ok(),
            };
        }

        static Task NotifyStackUpdateChangedAsync(
            IStackUpdateNotifier updateNotifier,
            string org,
            string project,
            string stack,
            string updateId,
            string reason,
            CancellationToken cancellationToken)
        {
            var message = new StackUpdateChangedMessage(
                org,
                project,
                stack,
                updateId,
                reason,
                DateTimeOffset.UtcNow);
            return updateNotifier.NotifyChangedAsync(message, cancellationToken);
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

            var tokenOrgId = context.User.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;
            var namespaceId = await NamespaceRoutingHelpers.ResolveAccessibleNamespaceIdAsync(
                store,
                userId,
                org,
                tokenOrgId);
            if (!string.IsNullOrWhiteSpace(namespaceId))
            {
                return (namespaceId, null);
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
                    Version: update.Version,
                    UpdateId: update.Id
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

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var config = await store.GetStackConfigByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            var latest = (await store.GetHistoryAsync(ids.Value.StackId)).FirstOrDefault();
            if (latest == default)
            {
                return Results.Ok(new LatestUpdateInfo(new UpdateInfo(Config: config)));
            }

            var (message, environment) = EndpointHelpers.ExtractMetadata(latest.ProgramRequestJson);
            var info = new UpdateInfo(
                Kind: latest.Kind,
                StartTime: EndpointHelpers.ToUnixTimeSeconds(latest.CreatedAt),
                Message: message,
                Environment: environment,
                Config: config,
                Result: EndpointHelpers.NormalizeUpdateStatus(latest.Status),
                EndTime: EndpointHelpers.ToUnixTimeSeconds(latest.CompletedAt),
                Version: latest.Version,
                UpdateId: latest.Id
            );
            return Results.Ok(new LatestUpdateInfo(info));
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
                Version: update.Value.Version,
                UpdateId: update.Value.Id
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

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}", async (HttpContext context, string org, string project, string stack, string updateId, StartUpdateRequest request, CancellationToken cancellationToken) =>
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

            var started = await store.StartUpdateAsync(
                updateId,
                ids.Value.StackId,
                JsonSerializer.Serialize(request, UpdateEndpointsJsonContext.Default.StartUpdateRequest));
            if (started.Status == BackendStore.StartUpdateStatus.NotFound)
            {
                return Results.NotFound();
            }

            if (started.Status == BackendStore.StartUpdateStatus.Conflict)
            {
                return Results.Conflict(new ErrorResponse(409, "another update is already in progress", null));
            }

            if (started.Status != BackendStore.StartUpdateStatus.Started || string.IsNullOrWhiteSpace(started.Token))
            {
                return Results.Conflict(new ErrorResponse(409, "update is not in a startable state", null));
            }

            var journalVersion = checkpointJournalingProvider.NegotiateJournalVersion(request.JournalVersion);
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "started",
                cancellationToken);
            return Results.Ok(new StartUpdateResponse(started.Version, started.Token, journalVersion));
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}", async (HttpContext context, string org, string project, string stack, string updateId, StartUpdateRequest request, CancellationToken cancellationToken) =>
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

            var started = await store.StartUpdateAsync(
                updateId,
                ids.Value.StackId,
                JsonSerializer.Serialize(request, UpdateEndpointsJsonContext.Default.StartUpdateRequest));
            if (started.Status == BackendStore.StartUpdateStatus.NotFound)
            {
                return Results.NotFound();
            }

            if (started.Status == BackendStore.StartUpdateStatus.Conflict)
            {
                return Results.Conflict(new ErrorResponse(409, "another update is already in progress", null));
            }

            if (started.Status != BackendStore.StartUpdateStatus.Started || string.IsNullOrWhiteSpace(started.Token))
            {
                return Results.Conflict(new ErrorResponse(409, "update is not in a startable state", null));
            }

            var journalVersion = checkpointJournalingProvider.NegotiateJournalVersion(request.JournalVersion);
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "started",
                cancellationToken);
            return Results.Ok(new StartUpdateResponse(started.Version, started.Token, journalVersion));
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

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/renew_lease", async (HttpContext context, string updateId, RenewUpdateLeaseRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            return Results.Ok(new RenewUpdateLeaseResponse(EndpointHelpers.GetUpdateToken(context) ?? string.Empty));
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/renew_lease", async (HttpContext context, string updateId, RenewUpdateLeaseRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            return Results.Ok(new RenewUpdateLeaseResponse(EndpointHelpers.GetUpdateToken(context) ?? string.Empty));
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/checkpoint", async (HttpContext context, string updateId, PatchUpdateCheckpointRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? Results.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/checkpoint", async (HttpContext context, string updateId, PatchUpdateCheckpointRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? Results.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/checkpointverbatim", async (HttpContext context, string updateId, PatchUpdateCheckpointVerbatimRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointVerbatimAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? Results.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/checkpointverbatim", async (HttpContext context, string updateId, PatchUpdateCheckpointVerbatimRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointVerbatimAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? Results.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/checkpointdelta", async (HttpContext context, string updateId, PatchUpdateCheckpointDeltaRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointDeltaAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? Results.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/checkpointdelta", async (HttpContext context, string updateId, PatchUpdateCheckpointDeltaRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointDeltaAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? Results.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/journalentries", async (HttpContext context, string updateId, JournalEntriesRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            await checkpointJournalingProvider.RecordJournalEntriesAsync(
                updateId,
                request.Entries,
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/journalentries", async (HttpContext context, string updateId, JournalEntriesRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            await checkpointJournalingProvider.RecordJournalEntriesAsync(
                updateId,
                request.Entries,
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events/batch", async (HttpContext context, string org, string project, string stack, string updateId, EngineEventBatch request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
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

            await store.RecordEventsAsync(updateId, eventsJson, cancellationToken);
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "events",
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events/batch", async (HttpContext context, string org, string project, string stack, string updateId, EngineEventBatch request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
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

            await store.RecordEventsAsync(updateId, eventsJson, cancellationToken);
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "events",
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events", async (string updateId, CancellationToken cancellationToken) =>
        {
            var events = await store.GetEventsAsync(updateId, cancellationToken);
            var jsonEvents = events.Select(ev => JsonDocument.Parse(ev).RootElement).ToArray();
            return Results.Ok(new GetUpdateEventsResponse(jsonEvents, null));
        });
        app.MapGet("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events", async (string updateId, CancellationToken cancellationToken) =>
        {
            var events = await store.GetEventsAsync(updateId, cancellationToken);
            var jsonEvents = events.Select(ev => JsonDocument.Parse(ev).RootElement).ToArray();
            return Results.Ok(new GetUpdateEventsResponse(jsonEvents, null));
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events", async (HttpContext context, string org, string project, string stack, string updateId, JsonElement request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            await store.RecordEventsAsync(updateId, new[] { request.GetRawText() }, cancellationToken);
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "events",
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events", async (HttpContext context, string org, string project, string stack, string updateId, JsonElement request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            await store.RecordEventsAsync(updateId, new[] { request.GetRawText() }, cancellationToken);
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "events",
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/cancel", async (HttpContext context, string org, string project, string stack, string updateId, CancellationToken cancellationToken) =>
        {
            await store.CancelUpdateAsync(updateId, cancellationToken);
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "cancelled",
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/cancel", async (HttpContext context, string org, string project, string stack, string updateId, CancellationToken cancellationToken) =>
        {
            await store.CancelUpdateAsync(updateId, cancellationToken);
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "cancelled",
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/complete", async (HttpContext context, string org, string project, string stack, string updateId, CompleteUpdateRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            await store.CompleteUpdateAsync(updateId, request.Status, cancellationToken);
            if (string.Equals(request.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var materialized = await checkpointJournalingProvider.FinalizeSucceededUpdateAsync(
                    updateId,
                    cancellationToken);
                var updateContext = await store.GetUpdateContextAsync(updateId, cancellationToken);
                if (!materialized && updateContext is { Kind: "destroy" })
                {
                    var latestDeployment = await store.GetLatestDeploymentAsync(
                        updateContext.Value.StackId,
                        cancellationToken);
                    var emptyDeployment = EndpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                    await store.SaveCheckpointAsync(
                        updateId,
                        emptyDeployment,
                        updateContext.Value.Version,
                        cancellationToken);
                }
            }
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "completed",
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/complete", async (HttpContext context, string org, string project, string stack, string updateId, CompleteUpdateRequest request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            await store.CompleteUpdateAsync(updateId, request.Status, cancellationToken);
            if (string.Equals(request.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            {
                var materialized = await checkpointJournalingProvider.FinalizeSucceededUpdateAsync(
                    updateId,
                    cancellationToken);
                var updateContext = await store.GetUpdateContextAsync(updateId, cancellationToken);
                if (!materialized && updateContext is { Kind: "destroy" })
                {
                    var latestDeployment = await store.GetLatestDeploymentAsync(
                        updateContext.Value.StackId,
                        cancellationToken);
                    var emptyDeployment = EndpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                    await store.SaveCheckpointAsync(
                        updateId,
                        emptyDeployment,
                        updateContext.Value.Version,
                        cancellationToken);
                }
            }
            await NotifyStackUpdateChangedAsync(
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "completed",
                cancellationToken);
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        return app;
    }
}
