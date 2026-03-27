using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Realtime;
using Makana.Api.Security;
using Makana.Api.Storage;
using Makana.Api.Updates;
using Makana.StackUpdates.Pulumi;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using ApiEngineEventBatch = Makana.Api.Contracts.EngineEventBatch;
using ApiGetUpdateEventsResponse = Makana.Api.Contracts.GetUpdateEventsResponse;

namespace Makana.Api.Endpoints;

internal static class UpdateEndpoints
{
    public static IEndpointRouteBuilder MapUpdateEndpoints(
        this IEndpointRouteBuilder app,
        StackCatalogQueryRepository stackQueries,
        UpdateCommandRepository updateCommands,
        UpdateQueryRepository updateQueries,
        UpdateLogRepository updateLog,
        IStackUpdateNotifier updateNotifier,
        IUpdateCheckpointJournalingProvider checkpointJournalingProvider,
        EphemeralStateJanitor ephemeralStateJanitor)
    {
        static bool RequireInProgressUpdateAsync(
            UpdateCommandRepository updateCommands,
            string updateId)
        {
            return updateCommands.IsUpdateInProgress(updateId);
        }

        static Conflict<ErrorResponse> UpdateNotInProgressResponse()
        {
            return TypedResults.Conflict(new ErrorResponse(409, "update is not in progress", null));
        }

        static IResult ToMutationError(UpdateMutationResult result)
        {
            var message = string.IsNullOrWhiteSpace(result.Message)
                ? "request could not be applied"
                : result.Message;
            return result.Status switch
            {
                UpdateMutationStatus.NotFound => TypedResults.NotFound(),
                UpdateMutationStatus.BadRequest => TypedResults.BadRequest(new ErrorResponse(400, message, null)),
                UpdateMutationStatus.Conflict => TypedResults.Conflict(new ErrorResponse(409, message, null)),
                _ => TypedResults.Ok(),
            };
        }

        static IResult InvalidEngineEventResponse()
            => TypedResults.BadRequest(new ErrorResponse(400, "engine event must contain exactly one payload", null));

        static bool DeploymentHasManagedResources(string? deploymentJson)
        {
            if (string.IsNullOrWhiteSpace(deploymentJson))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(deploymentJson);
                if (!doc.RootElement.TryGetProperty("resources", out var resourcesElement) ||
                    resourcesElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                foreach (var resource in resourcesElement.EnumerateArray())
                {
                    if (resource.ValueKind != JsonValueKind.Object ||
                        !resource.TryGetProperty("type", out var typeElement) ||
                        typeElement.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    if (!string.Equals(typeElement.GetString(), "pulumi:pulumi:Stack", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch (JsonException)
            {
                return true;
            }

            return false;
        }

        static Task NotifyStackUpdateChangedWithSequenceAsync(
            UpdateCommandRepository updateCommands,
            UpdateLogRepository updateLog,
            IStackUpdateNotifier updateNotifier,
            string org,
            string project,
            string stack,
            string updateId,
            string reason,
            long? afterSequence,
            CancellationToken cancellationToken)
            => NotifyStackUpdateChangedCoreAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                reason,
                afterSequence,
                resolveLatestSequence: false,
                cancellationToken);

        static Task NotifyStackUpdateChangedAsync(
            UpdateCommandRepository updateCommands,
            UpdateLogRepository updateLog,
            IStackUpdateNotifier updateNotifier,
            string org,
            string project,
            string stack,
            string updateId,
            string reason,
            CancellationToken cancellationToken)
            => NotifyStackUpdateChangedCoreAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                reason,
                afterSequence: null,
                resolveLatestSequence: true,
                cancellationToken);

        static async Task NotifyStackUpdateChangedCoreAsync(
            UpdateCommandRepository updateCommands,
            UpdateLogRepository updateLog,
            IStackUpdateNotifier updateNotifier,
            string org,
            string project,
            string stack,
            string updateId,
            string reason,
            long? afterSequence,
            bool resolveLatestSequence,
            CancellationToken cancellationToken)
        {
            var updateContext = updateCommands.GetUpdateContext(updateId);
            if (updateContext is null)
            {
                return;
            }

            var effectiveAfterSequence = afterSequence;
            if (resolveLatestSequence)
            {
                effectiveAfterSequence = updateLog.GetLatestEventSequence(updateId);
            }

            var message = new StackUpdateChangedMessage(
                org,
                project,
                stack,
                updateContext.Value.StackId,
                updateId,
                effectiveAfterSequence,
                reason,
                DateTimeOffset.UtcNow);
            await updateNotifier.NotifyChangedAsync(message, cancellationToken);
        }

        static string SerializeEvent(EngineEvent engineEvent)
            => JsonSerializer.Serialize(engineEvent, PulumiEngineEventJsonContext.Default.EngineEvent);

        static EngineEvent[] DeserializeStoredEvents(IEnumerable<string> storedEvents)
        {
            var parsed = new List<EngineEvent>();
            foreach (var storedEvent in storedEvents)
            {
                var engineEvent = JsonSerializer.Deserialize(storedEvent, PulumiEngineEventJsonContext.Default.EngineEvent);
                if (engineEvent is null || !engineEvent.HasSinglePayload)
                {
                    continue;
                }

                parsed.Add(engineEvent);
            }

            return [.. parsed];
        }

        static bool TryParseContinuationToken(string? continuationToken, out long? afterSequence)
        {
            afterSequence = null;
            if (string.IsNullOrWhiteSpace(continuationToken))
            {
                return true;
            }

            if (!long.TryParse(continuationToken, out var parsed) || parsed < 0)
            {
                return false;
            }

            afterSequence = parsed;
            return true;
        }

        static async Task<ApiGetUpdateEventsResponse> ReadUpdateEventsResponseAsync(
            UpdateCommandRepository updateCommands,
            UpdateLogRepository updateLog,
            string updateId,
            long? afterSequence,
            CancellationToken cancellationToken)
        {
            const int pageSize = 1024;
            const int maxWaitIterations = 10;
            var lastObservedSequence = afterSequence;

            for (var attempt = 0; attempt <= maxWaitIterations; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var page = updateLog.GetEventPage(updateId, afterSequence, pageSize);
                if (page.EventsJson.Count > 0)
                {
                    var continuation = updateCommands.IsUpdateInProgress(updateId)
                        ? (page.LastSequence ?? lastObservedSequence ?? 0).ToString()
                        : null;
                    return new ApiGetUpdateEventsResponse(DeserializeStoredEvents(page.EventsJson), continuation);
                }

                var latestSequence = updateLog.GetLatestEventSequence(updateId);
                lastObservedSequence = latestSequence ?? lastObservedSequence;
                if (!updateCommands.IsUpdateInProgress(updateId))
                {
                    return new ApiGetUpdateEventsResponse([], null);
                }

                if (attempt == maxWaitIterations)
                {
                    var continuation = (latestSequence ?? afterSequence ?? 0).ToString();
                    return new ApiGetUpdateEventsResponse([], continuation);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            }

            return new ApiGetUpdateEventsResponse([], null);
        }

        app.MapGet(StackRouteConventions.ApiStackRoute + "/updates", async Task<Results<NotFound, Ok<GetHistoryResponse>>> (string org, string project, string stack, bool? includePreviews, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            var stackIds = stackQueries.GetStackIds(org, project, stack);
            if (stackIds is null)
            {
                return TypedResults.NotFound();
            }

            var updates = updateQueries.GetHistory(stackIds.Value.StackId, includePreviews: true);
            var previewFlags = updateQueries.GetUpdatePreviewFlags([.. updates.Select(static update => update.Id)]);
            var updateInfos = updates
                .Where(update => includePreviews == true || !endpointHelpers.IsPreviewOperation(update.Kind, update.ProgramRequestJson, previewFlags.GetValueOrDefault(update.Id)))
                .Select(update =>
            {
                var config = endpointHelpers.ExtractConfig(update.ProgramRequestJson);
                var (message, environment) = endpointHelpers.ExtractMetadata(update.ProgramRequestJson);
                var isPreview = endpointHelpers.IsPreviewOperation(update.Kind, update.ProgramRequestJson, previewFlags.GetValueOrDefault(update.Id));
                return new UpdateInfo(
                    Kind: update.Kind,
                    IsPreview: isPreview,
                    StartTime: endpointHelpers.ToUnixTimeSeconds(update.CreatedAt),
                    Message: message,
                    Environment: environment,
                    Config: config,
                    Result: endpointHelpers.NormalizeUpdateResult(update.Status),
                    EndTime: endpointHelpers.ToUnixTimeSeconds(update.CompletedAt),
                    Version: update.Version,
                    UpdateId: update.Id
                );
            }).ToArray();
            return TypedResults.Ok(new GetHistoryResponse(updateInfos));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet(StackRouteConventions.ApiStackRoute + "/updates/latest", async Task<Results<NotFound, Ok<LatestUpdateInfo>, Ok<UpdateServiceEnvelope>>> (string org, string project, string stack, [FromQuery(Name = "output-type")] string? outputType, EndpointHelpers endpointHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            var config = stackQueries.GetStackConfig(org, project, stack);
            var latest = updateQueries.GetHistory(ids.Value.StackId, includePreviews: true).FirstOrDefault();
            if (latest == default)
            {
                return TypedResults.Ok(new LatestUpdateInfo(new UpdateInfo(Config: config)));
            }

            if (string.Equals(outputType, "service", StringComparison.OrdinalIgnoreCase))
            {
                var envelope = await versionedUpdatePageMaterializer.BuildServiceEnvelopeAsync(ids.Value.StackId, latest.Id, cancellationToken);
                return envelope is null ? TypedResults.NotFound() : TypedResults.Ok(envelope);
            }

            var isPreview = endpointHelpers.IsPreviewOperation(
                latest.Kind,
                latest.ProgramRequestJson,
                await versionedUpdatePageMaterializer.IsPreviewAsync(latest.Id, cancellationToken));
            var (message, environment) = endpointHelpers.ExtractMetadata(latest.ProgramRequestJson);
            var info = new UpdateInfo(
                Kind: latest.Kind,
                IsPreview: isPreview,
                StartTime: endpointHelpers.ToUnixTimeSeconds(latest.CreatedAt),
                Message: message,
                Environment: environment,
                Config: config,
                Result: endpointHelpers.NormalizeUpdateResult(latest.Status),
                EndTime: endpointHelpers.ToUnixTimeSeconds(latest.CompletedAt),
                Version: latest.Version,
                UpdateId: latest.Id
            );
            return TypedResults.Ok(new LatestUpdateInfo(info));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet(StackRouteConventions.ApiStackRoute + "/overview", async Task<Results<NotFound, Ok<StackOverviewResponse>>> (
            string org,
            string project,
            string stack,
            StackOverviewMaterializer stackOverviewMaterializer,
            CancellationToken cancellationToken) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            var overview = await stackOverviewMaterializer.BuildAsync(ids.Value.StackId, cancellationToken);
            return TypedResults.Ok(overview);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet(StackRouteConventions.ApiStackRoute + "/updates/{version}", async Task<Results<NotFound, Ok<UpdateInfo>, Ok<UpdateServiceEnvelope>>> (string org, string project, string stack, int version, [FromQuery(Name = "output-type")] string? outputType, EndpointHelpers endpointHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            var update = updateQueries.GetUpdateByVersion(ids.Value.StackId, version);
            if (update is null)
            {
                return TypedResults.NotFound();
            }

            if (string.Equals(outputType, "service", StringComparison.OrdinalIgnoreCase))
            {
                var envelope = await versionedUpdatePageMaterializer.BuildServiceEnvelopeAsync(ids.Value.StackId, update.Value.Id, cancellationToken);
                return envelope is null ? TypedResults.NotFound() : TypedResults.Ok(envelope);
            }

            var isPreview = endpointHelpers.IsPreviewOperation(
                update.Value.Kind,
                update.Value.ProgramRequestJson,
                await versionedUpdatePageMaterializer.IsPreviewAsync(update.Value.Id, cancellationToken));
            var config = endpointHelpers.ExtractConfig(update.Value.ProgramRequestJson);
            var (message, environment) = endpointHelpers.ExtractMetadata(update.Value.ProgramRequestJson);
            var resourcesResponse = await versionedUpdatePageMaterializer.BuildResourcesForVersionAsync(ids.Value.StackId, version, cancellationToken);
            var info = new UpdateInfo(
                Kind: update.Value.Kind,
                IsPreview: isPreview,
                StartTime: endpointHelpers.ToUnixTimeSeconds(update.Value.CreatedAt),
                Message: message,
                Environment: environment,
                Config: config,
                Result: endpointHelpers.NormalizeUpdateResult(update.Value.Status),
                EndTime: endpointHelpers.ToUnixTimeSeconds(update.Value.CompletedAt),
                Version: update.Value.Version,
                Resources: resourcesResponse?.Resources.Select(static resource => resource.Resource).ToArray(),
                UpdateId: update.Value.Id
            );
            return TypedResults.Ok(info);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet(StackRouteConventions.ApiStackRoute + "/updates/{version}/reference", async Task<Results<NotFound, Ok<SelectedUpdateReferenceResponse>>> (string org, string project, string stack, int version, EndpointHelpers endpointHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            var update = updateQueries.GetUpdateByVersion(ids.Value.StackId, version);
            if (update is null)
            {
                return TypedResults.NotFound();
            }

            var isPreview = endpointHelpers.IsPreviewOperation(
                update.Value.Kind,
                update.Value.ProgramRequestJson,
                await versionedUpdatePageMaterializer.IsPreviewAsync(update.Value.Id, cancellationToken));

            return TypedResults.Ok(new SelectedUpdateReferenceResponse(
                UpdateId: update.Value.Id,
                Version: update.Value.Version,
                Kind: update.Value.Kind,
                IsPreview: isPreview,
                RouteRunType: endpointHelpers.GetUpdateRouteRunType(update.Value.Kind, isPreview) ?? "updates",
                RouteReference: endpointHelpers.GetUpdateRouteReference(update.Value.Id, update.Value.Version, update.Value.Kind, isPreview)
                    ?? update.Value.Id));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet(StackRouteConventions.ApiStackRoute + "/updates/{version}/summary", async Task<Results<NotFound, Ok<UpdateVersionSummary>>> (string org, string project, string stack, int version, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            var summary = await versionedUpdatePageMaterializer.BuildVersionSummaryAsync(ids.Value.StackId, version, cancellationToken);
            return summary is null ? TypedResults.NotFound() : TypedResults.Ok(summary);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet(StackRouteConventions.ApiStackRoute + "/updates/{version}/contents/files", (string org, string project, string stack, int version) =>
        {
            return TypedResults.Ok(new UpdateContentsFilesResponse([]));
        });

        app.MapGet(StackRouteConventions.ApiStackRoute + "/updates/{version}/contents/file/{*path}", () =>
        {
            return TypedResults.NotFound();
        });

        app.MapPost(StackRouteConventions.ApiStackRoute + "/update", async (string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers) =>
        {
            return await endpointHelpers.CreateUpdateResponseAsync(
                stackQueries, updateCommands, org, project, stack, "update", request);
        }).RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapPost(StackRouteConventions.ApiStackRoute + "/preview", async (string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers) =>
        {
            return await endpointHelpers.CreateUpdateResponseAsync(
                stackQueries, updateCommands, org, project, stack, "preview", request);
        }).RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapPost(StackRouteConventions.ApiStackRoute + "/refresh", async (string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers) =>
        {
            return await endpointHelpers.CreateUpdateResponseAsync(
                stackQueries, updateCommands, org, project, stack, "refresh", request);
        }).RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapPost(StackRouteConventions.ApiStackRoute + "/destroy", async (string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers) =>
        {
            return await endpointHelpers.CreateUpdateResponseAsync(
                stackQueries, updateCommands, org, project, stack, "destroy", request);
        }).RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapPost(StackRouteConventions.ApiStackRoute + "/update/{updateId}", async Task<Results<NotFound, Conflict<ErrorResponse>, Ok<StartUpdateResponse>>> (string org, string project, string stack, string updateId, StartUpdateRequest request, CancellationToken cancellationToken) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            await ephemeralStateJanitor.CancelExpiredUpdatesForStackAsync(ids.Value.StackId, cancellationToken);
            var started = updateCommands.StartUpdate(
                updateId,
                ids.Value.StackId,
                JsonSerializer.Serialize(request, UpdateEndpointsJsonContext.Default.StartUpdateRequest));
            if (started.Status == UpdateStartStatus.NotFound)
            {
                return TypedResults.NotFound();
            }

            if (started.Status == UpdateStartStatus.Conflict)
            {
                return TypedResults.Conflict(new ErrorResponse(409, "another update is already in progress", null));
            }

            if (started.Status != UpdateStartStatus.Started || string.IsNullOrWhiteSpace(started.Token))
            {
                return TypedResults.Conflict(new ErrorResponse(409, "update is not in a startable state", null));
            }

            var journalVersion = checkpointJournalingProvider.NegotiateJournalVersion(request.JournalVersion);
            await NotifyStackUpdateChangedAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "started",
                cancellationToken);
            return TypedResults.Ok(new StartUpdateResponse(started.Version, started.Token, journalVersion));
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");
        app.MapPost(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}", async Task<Results<NotFound, Conflict<ErrorResponse>, Ok<StartUpdateResponse>>> (string org, string project, string stack, string updateId, StartUpdateRequest request, CancellationToken cancellationToken) =>
        {
            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            await ephemeralStateJanitor.CancelExpiredUpdatesForStackAsync(ids.Value.StackId, cancellationToken);
            var started = updateCommands.StartUpdate(
                updateId,
                ids.Value.StackId,
                JsonSerializer.Serialize(request, UpdateEndpointsJsonContext.Default.StartUpdateRequest));
            if (started.Status == UpdateStartStatus.NotFound)
            {
                return TypedResults.NotFound();
            }

            if (started.Status == UpdateStartStatus.Conflict)
            {
                return TypedResults.Conflict(new ErrorResponse(409, "another update is already in progress", null));
            }

            if (started.Status != UpdateStartStatus.Started || string.IsNullOrWhiteSpace(started.Token))
            {
                return TypedResults.Conflict(new ErrorResponse(409, "update is not in a startable state", null));
            }

            var journalVersion = checkpointJournalingProvider.NegotiateJournalVersion(request.JournalVersion);
            await NotifyStackUpdateChangedAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "started",
                cancellationToken);
            return TypedResults.Ok(new StartUpdateResponse(started.Version, started.Token, journalVersion));
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapGet(StackRouteConventions.ApiStackRoute + "/update/{updateId}", async Task<Results<Ok<UpdateResults>, NotFound>> (string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, CancellationToken cancellationToken) =>
        {
            var updateContext = updateCommands.GetUpdateContext(updateId);
            if (updateContext is null)
            {
                return TypedResults.NotFound();
            }

            var recoveredState = await updateViewMaterializer.BuildRecoveredStateAsync(updateContext.Value.StackId, updateId, cancellationToken);
            if (recoveredState is null)
            {
                return TypedResults.NotFound();
            }

            var stableToken = recoveredState.IsInProgress
                ? (string.IsNullOrWhiteSpace(continuationToken) ? recoveredState.ContinuationToken ?? "0" : continuationToken)
                : null;
            return TypedResults.Ok(new UpdateResults(recoveredState.Info.Status, recoveredState.Info.Version, [], stableToken));
        });
        app.MapGet(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}", async Task<Results<Ok<UpdateResults>, NotFound>> (string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, CancellationToken cancellationToken) =>
        {
            var updateContext = updateCommands.GetUpdateContext(updateId);
            if (updateContext is null)
            {
                return TypedResults.NotFound();
            }

            var recoveredState = await updateViewMaterializer.BuildRecoveredStateAsync(updateContext.Value.StackId, updateId, cancellationToken);
            if (recoveredState is null)
            {
                return TypedResults.NotFound();
            }

            var stableToken = recoveredState.IsInProgress
                ? (string.IsNullOrWhiteSpace(continuationToken) ? recoveredState.ContinuationToken ?? "0" : continuationToken)
                : null;
            return TypedResults.Ok(new UpdateResults(recoveredState.Info.Status, recoveredState.Info.Version, [], stableToken));
        });
        app.MapGet(StackRouteConventions.ApiStackRoute + "/update/{updateId}/view", async Task<Results<BadRequest<ErrorResponse>, NotFound, Ok<UpdateViewResponse>>> (string org, string project, string stack, string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return TypedResults.BadRequest(new ErrorResponse(Code: 400, Message: "invalid continuation token", RequestId: null));
            }

            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            var view = await updateViewMaterializer.BuildAsync(ids.Value.StackId, updateId, afterSequence, cancellationToken);
            return view is null ? TypedResults.NotFound() : TypedResults.Ok(view);
        }).RequireAuthorization("OrgRouteAccess");
        app.MapGet(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/view", async Task<Results<BadRequest<ErrorResponse>, NotFound, Ok<UpdateViewResponse>>> (string org, string project, string stack, string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return TypedResults.BadRequest(new ErrorResponse(Code: 400, Message: "invalid continuation token", RequestId: null));
            }

            var ids = stackQueries.GetStackIds(org, project, stack);
            if (ids is null)
            {
                return TypedResults.NotFound();
            }

            var view = await updateViewMaterializer.BuildAsync(ids.Value.StackId, updateId, afterSequence, cancellationToken);
            return view is null ? TypedResults.NotFound() : TypedResults.Ok(view);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost(StackRouteConventions.ApiStackRoute + "/update/{updateId}/renew_lease", Results<Conflict<ErrorResponse>, NotFound, Ok<RenewUpdateLeaseResponse>> (string updateId, RenewUpdateLeaseRequest request, AuthenticatedApiContextAccessor authenticatedApiContextAccessor, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var token = authenticatedApiContextAccessor.GetCurrent().UpdateToken ?? string.Empty;
            var expiration = updateCommands.RenewUpdateTokenLease(updateId, token, request.Duration);
            if (expiration is null)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(new RenewUpdateLeaseResponse(token, expiration.Value));
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/renew_lease", Results<Conflict<ErrorResponse>, NotFound, Ok<RenewUpdateLeaseResponse>> (string updateId, RenewUpdateLeaseRequest request, AuthenticatedApiContextAccessor authenticatedApiContextAccessor, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var token = authenticatedApiContextAccessor.GetCurrent().UpdateToken ?? string.Empty;
            var expiration = updateCommands.RenewUpdateTokenLease(updateId, token, request.Duration);
            if (expiration is null)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(new RenewUpdateLeaseResponse(token, expiration.Value));
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch(StackRouteConventions.ApiStackRoute + "/update/{updateId}/checkpoint", async (HttpContext context, string updateId, PatchUpdateCheckpointRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? TypedResults.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/checkpoint", async (HttpContext context, string updateId, PatchUpdateCheckpointRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? TypedResults.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch(StackRouteConventions.ApiStackRoute + "/update/{updateId}/checkpointverbatim", async (HttpContext context, string updateId, PatchUpdateCheckpointVerbatimRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointVerbatimAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? TypedResults.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/checkpointverbatim", async (HttpContext context, string updateId, PatchUpdateCheckpointVerbatimRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointVerbatimAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? TypedResults.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch(StackRouteConventions.ApiStackRoute + "/update/{updateId}/checkpointdelta", async (HttpContext context, string updateId, PatchUpdateCheckpointDeltaRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointDeltaAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? TypedResults.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/checkpointdelta", async (HttpContext context, string updateId, PatchUpdateCheckpointDeltaRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var result = await checkpointJournalingProvider.ApplyCheckpointDeltaAsync(
                updateId,
                request,
                cancellationToken);
            return result.Status == UpdateMutationStatus.Ok ? TypedResults.Ok() : ToMutationError(result);
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch(StackRouteConventions.ApiStackRoute + "/update/{updateId}/journalentries", async Task<Results<Ok, Conflict<ErrorResponse>>> (HttpContext context, string updateId, JournalEntriesRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await checkpointJournalingProvider.RecordJournalEntriesAsync(
                updateId,
                request.Entries,
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPatch(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/journalentries", async Task<Results<Ok, Conflict<ErrorResponse>>> (HttpContext context, string updateId, JournalEntriesRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            await checkpointJournalingProvider.RecordJournalEntriesAsync(
                updateId,
                request.Entries,
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPost(StackRouteConventions.ApiStackRoute + "/update/{updateId}/events/batch", async (HttpContext context, string org, string project, string stack, string updateId, ApiEngineEventBatch request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            if (request.Events.Any(static ev => !ev.HasSinglePayload))
            {
                return InvalidEngineEventResponse();
            }

            var eventsJson = request.Events.Select(SerializeEvent).ToArray();
            updateLog.RecordEvents(updateId, eventsJson);
            var afterSequence = request.Events.Length == 0
                ? (long?)null
                : Math.Max(0, request.Events.Min(static ev => ev.Sequence) - 1L);
            await NotifyStackUpdateChangedWithSequenceAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "events",
                afterSequence,
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/events/batch", async (HttpContext context, string org, string project, string stack, string updateId, ApiEngineEventBatch request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            if (request.Events.Any(static ev => !ev.HasSinglePayload))
            {
                return InvalidEngineEventResponse();
            }

            var eventsJson = request.Events.Select(SerializeEvent).ToArray();
            updateLog.RecordEvents(updateId, eventsJson);
            var afterSequence = request.Events.Length == 0
                ? (long?)null
                : Math.Max(0, request.Events.Min(static ev => ev.Sequence) - 1L);
            await NotifyStackUpdateChangedWithSequenceAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "events",
                afterSequence,
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapGet(StackRouteConventions.ApiStackRoute + "/update/{updateId}/events", async Task<Results<Ok<ApiGetUpdateEventsResponse>, BadRequest<ErrorResponse>>> (string updateId, string? continuationToken, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return TypedResults.BadRequest(new ErrorResponse(400, "invalid continuation token", null));
            }

            var response = await ReadUpdateEventsResponseAsync(updateCommands, updateLog, updateId, afterSequence, cancellationToken);
            return TypedResults.Ok(response);
        });
        app.MapGet(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/events", async Task<Results<Ok<ApiGetUpdateEventsResponse>, BadRequest<ErrorResponse>>> (string updateId, string? continuationToken, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return TypedResults.BadRequest(new ErrorResponse(400, "invalid continuation token", null));
            }

            var response = await ReadUpdateEventsResponseAsync(updateCommands, updateLog, updateId, afterSequence, cancellationToken);
            return TypedResults.Ok(response);
        });

        app.MapPost(StackRouteConventions.ApiStackRoute + "/update/{updateId}/events", async (HttpContext context, string org, string project, string stack, string updateId, EngineEvent request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            if (!request.HasSinglePayload)
            {
                return InvalidEngineEventResponse();
            }

            updateLog.RecordEvents(updateId, [SerializeEvent(request)]);
            await NotifyStackUpdateChangedWithSequenceAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "events",
                Math.Max(0, request.Sequence - 1L),
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/events", async (HttpContext context, string org, string project, string stack, string updateId, EngineEvent request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            if (!request.HasSinglePayload)
            {
                return InvalidEngineEventResponse();
            }

            updateLog.RecordEvents(updateId, [SerializeEvent(request)]);
            await NotifyStackUpdateChangedWithSequenceAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "events",
                Math.Max(0, request.Sequence - 1L),
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPost(StackRouteConventions.ApiStackRoute + "/update/{updateId}/cancel", async (HttpContext context, string org, string project, string stack, string updateId, CancellationToken cancellationToken) =>
        {
            await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(updateId, cancellationToken);
            updateCommands.CancelUpdate(updateId);
            await NotifyStackUpdateChangedAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "cancelled",
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");
        app.MapPost(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/cancel", async (HttpContext context, string org, string project, string stack, string updateId, CancellationToken cancellationToken) =>
        {
            await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(updateId, cancellationToken);
            updateCommands.CancelUpdate(updateId);
            await NotifyStackUpdateChangedAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "cancelled",
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapPost(StackRouteConventions.ApiStackRoute + "/update/{updateId}/complete", async Task<Results<Conflict<ErrorResponse>, Ok>> (HttpContext context, string org, string project, string stack, string updateId, CompleteUpdateRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var isSucceeded = string.Equals(request.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
            if (!isSucceeded)
            {
                await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(updateId, cancellationToken);
            }

            updateCommands.CompleteUpdate(updateId, request.Status);
            if (isSucceeded)
            {
                var materialized = await checkpointJournalingProvider.FinalizeSucceededUpdateAsync(
                    updateId,
                    cancellationToken);
                var updateContext = updateCommands.GetUpdatePersistenceContext(updateId);
                if (!materialized &&
                    updateContext is { Kind: "destroy" } &&
                    !endpointHelpers.IsPreviewProgramRequest(updateContext.Value.ProgramRequestJson))
                {
                    var latestDeployment = updateQueries.GetLatestDeployment(
                        updateContext.Value.StackId);
                    var emptyDeployment = endpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                    updateLog.SaveCheckpoint(
                        updateId,
                        emptyDeployment,
                        updateContext.Value.Version);
                }
                else if (updateContext is { Kind: "destroy" } &&
                         !endpointHelpers.IsPreviewProgramRequest(updateContext.Value.ProgramRequestJson))
                {
                    var latestDeployment = updateQueries.GetLatestDeployment(
                        updateContext.Value.StackId);
                    if (DeploymentHasManagedResources(latestDeployment?.DeploymentJson))
                    {
                        var emptyDeployment = endpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                        updateLog.SaveCheckpoint(
                            updateId,
                            emptyDeployment,
                            updateContext.Value.Version);
                    }
                }
            }
            await NotifyStackUpdateChangedAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "completed",
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost(StackRouteConventions.ApiStackRoute + "/{updateKind}/{updateId}/complete", async Task<Results<Conflict<ErrorResponse>, Ok>> (HttpContext context, string org, string project, string stack, string updateId, CompleteUpdateRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var isSucceeded = string.Equals(request.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
            if (!isSucceeded)
            {
                await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(updateId, cancellationToken);
            }

            updateCommands.CompleteUpdate(updateId, request.Status);
            if (isSucceeded)
            {
                var materialized = await checkpointJournalingProvider.FinalizeSucceededUpdateAsync(
                    updateId,
                    cancellationToken);
                var updateContext = updateCommands.GetUpdatePersistenceContext(updateId);
                if (!materialized &&
                    updateContext is { Kind: "destroy" } &&
                    !endpointHelpers.IsPreviewProgramRequest(updateContext.Value.ProgramRequestJson))
                {
                    var latestDeployment = updateQueries.GetLatestDeployment(
                        updateContext.Value.StackId);
                    var emptyDeployment = endpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                    updateLog.SaveCheckpoint(
                        updateId,
                        emptyDeployment,
                        updateContext.Value.Version);
                }
                else if (updateContext is { Kind: "destroy" } &&
                         !endpointHelpers.IsPreviewProgramRequest(updateContext.Value.ProgramRequestJson))
                {
                    var latestDeployment = updateQueries.GetLatestDeployment(
                        updateContext.Value.StackId);
                    if (DeploymentHasManagedResources(latestDeployment?.DeploymentJson))
                    {
                        var emptyDeployment = endpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                        updateLog.SaveCheckpoint(
                            updateId,
                            emptyDeployment,
                            updateContext.Value.Version);
                    }
                }
            }
            await NotifyStackUpdateChangedAsync(
                updateCommands,
                updateLog,
                updateNotifier,
                org,
                project,
                stack,
                updateId,
                "completed",
                cancellationToken);
            return TypedResults.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        return app;
    }
}
