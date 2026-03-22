using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Realtime;
using Makana.Api.Security;
using Makana.Api.Storage;
using Makana.Api.Updates;
using Makana.StackUpdates.Pulumi;
using Microsoft.AspNetCore.Mvc;
using ApiEngineEventBatch = Makana.Api.Contracts.EngineEventBatch;
using ApiGetUpdateEventsResponse = Makana.Api.Contracts.GetUpdateEventsResponse;

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

        static IResult InvalidEngineEventResponse()
            => Results.BadRequest(new ErrorResponse(400, "engine event must contain exactly one payload", null));

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

            return parsed.ToArray();
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
            BackendStore store,
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

                var page = await store.GetEventPageAsync(updateId, afterSequence, pageSize, cancellationToken);
                if (page.EventsJson.Count > 0)
                {
                    var continuation = await store.IsUpdateInProgressAsync(updateId, cancellationToken)
                        ? (page.LastSequence ?? lastObservedSequence ?? 0).ToString()
                        : null;
                    return new ApiGetUpdateEventsResponse(DeserializeStoredEvents(page.EventsJson), continuation);
                }

                var latestSequence = await store.GetLatestEventSequenceAsync(updateId, cancellationToken);
                lastObservedSequence = latestSequence ?? lastObservedSequence;
                if (!await store.IsUpdateInProgressAsync(updateId, cancellationToken))
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

        static async Task<(string? NamespaceId, IResult? Failure)> ResolveNamespaceIdForRouteAsync(
            HttpContext context, NamespaceRoutingHelpers namespaceRoutingHelpers, string org)
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

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates", async (HttpContext context, string org, string project, string stack, bool? includePreviews, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var stackIds = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (stackIds is null)
            {
                return Results.NotFound();
            }

            var updates = await store.GetHistoryAsync(stackIds.Value.StackId, includePreviews: true);
            var previewFlags = await store.GetUpdatePreviewFlagsAsync(updates.Select(static update => update.Id).ToArray(), cancellationToken);
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
            return Results.Ok(new GetHistoryResponse(updateInfos));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/latest", async (HttpContext context, string org, string project, string stack, [FromQuery(Name = "output-type")] string? outputType, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
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
            var latest = (await store.GetHistoryAsync(ids.Value.StackId, includePreviews: true)).FirstOrDefault();
            if (latest == default)
            {
                return Results.Ok(new LatestUpdateInfo(new UpdateInfo(Config: config)));
            }

            if (string.Equals(outputType, "service", StringComparison.OrdinalIgnoreCase))
            {
                var envelope = await versionedUpdatePageMaterializer.BuildServiceEnvelopeAsync(ids.Value.StackId, latest.Id, cancellationToken);
                return envelope is null ? Results.NotFound() : Results.Ok(envelope);
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
            return Results.Ok(new LatestUpdateInfo(info));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/overview", async (
            HttpContext context,
            string org,
            string project,
            string stack,
            NamespaceRoutingHelpers namespaceRoutingHelpers,
            StackOverviewMaterializer stackOverviewMaterializer,
            CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var overview = await stackOverviewMaterializer.BuildAsync(ids.Value.StackId, cancellationToken);
            return Results.Ok(overview);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}", async (HttpContext context, string org, string project, string stack, int version, [FromQuery(Name = "output-type")] string? outputType, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
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

            if (string.Equals(outputType, "service", StringComparison.OrdinalIgnoreCase))
            {
                var envelope = await versionedUpdatePageMaterializer.BuildServiceEnvelopeAsync(ids.Value.StackId, update.Value.Id, cancellationToken);
                return envelope is null ? Results.NotFound() : Results.Ok(envelope);
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
            return Results.Ok(info);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}/reference", async (HttpContext context, string org, string project, string stack, int version, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
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

            var isPreview = endpointHelpers.IsPreviewOperation(
                update.Value.Kind,
                update.Value.ProgramRequestJson,
                await versionedUpdatePageMaterializer.IsPreviewAsync(update.Value.Id, cancellationToken));

            return Results.Ok(new SelectedUpdateReferenceResponse(
                UpdateId: update.Value.Id,
                Version: update.Value.Version,
                Kind: update.Value.Kind,
                IsPreview: isPreview,
                RouteRunType: endpointHelpers.GetUpdateRouteRunType(update.Value.Kind, isPreview) ?? "updates",
                RouteReference: endpointHelpers.GetUpdateRouteReference(update.Value.Id, update.Value.Version, update.Value.Kind, isPreview)
                    ?? update.Value.Id));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}/summary", async (HttpContext context, string org, string project, string stack, int version, NamespaceRoutingHelpers namespaceRoutingHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var summary = await versionedUpdatePageMaterializer.BuildVersionSummaryAsync(ids.Value.StackId, version, cancellationToken);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}/contents/files", (string org, string project, string stack, int version) =>
        {
            return Results.Ok(new UpdateContentsFilesResponse(Array.Empty<string>()));
        });

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}/contents/file/{*path}", () =>
        {
            return Results.NotFound();
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await endpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                store, namespaceResolution.NamespaceId!, project, stack, "update", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/preview", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await endpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                store, namespaceResolution.NamespaceId!, project, stack, "preview", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/refresh", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await endpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                store, namespaceResolution.NamespaceId!, project, stack, "refresh", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/destroy", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await endpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                store, namespaceResolution.NamespaceId!, project, stack, "destroy", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}", async (HttpContext context, string org, string project, string stack, string updateId, StartUpdateRequest request, NamespaceRoutingHelpers namespaceRoutingHelpers, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
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
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}", async (HttpContext context, string org, string project, string stack, string updateId, StartUpdateRequest request, NamespaceRoutingHelpers namespaceRoutingHelpers, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
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

        app.MapGet("/api/stacks/{org}/{project}/{stack}/update/{updateId}", async (string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, CancellationToken cancellationToken) =>
        {
            var updateContext = await store.GetUpdateContextAsync(updateId, cancellationToken);
            if (updateContext is null)
            {
                return Results.NotFound();
            }

            var recoveredState = await updateViewMaterializer.BuildRecoveredStateAsync(updateContext.Value.StackId, updateId, cancellationToken);
            if (recoveredState is null)
            {
                return Results.NotFound();
            }

            var stableToken = recoveredState.IsInProgress
                ? (string.IsNullOrWhiteSpace(continuationToken) ? recoveredState.ContinuationToken ?? "0" : continuationToken)
                : null;
            return Results.Ok(new UpdateResults(recoveredState.Info.Status, recoveredState.Info.Version, [], stableToken));
        });
        app.MapGet("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}", async (string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, CancellationToken cancellationToken) =>
        {
            var updateContext = await store.GetUpdateContextAsync(updateId, cancellationToken);
            if (updateContext is null)
            {
                return Results.NotFound();
            }

            var recoveredState = await updateViewMaterializer.BuildRecoveredStateAsync(updateContext.Value.StackId, updateId, cancellationToken);
            if (recoveredState is null)
            {
                return Results.NotFound();
            }

            var stableToken = recoveredState.IsInProgress
                ? (string.IsNullOrWhiteSpace(continuationToken) ? recoveredState.ContinuationToken ?? "0" : continuationToken)
                : null;
            return Results.Ok(new UpdateResults(recoveredState.Info.Status, recoveredState.Info.Version, [], stableToken));
        });
        app.MapGet("/api/stacks/{org}/{project}/{stack}/update/{updateId}/view", async (HttpContext context, string org, string project, string stack, string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, NamespaceRoutingHelpers namespaceRoutingHelpers, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return Results.BadRequest(new ErrorResponse(Code: 400, Message: "invalid continuation token", RequestId: null));
            }

            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var view = await updateViewMaterializer.BuildAsync(ids.Value.StackId, updateId, afterSequence, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        }).RequireAuthorization("OrgRouteAccess");
        app.MapGet("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/view", async (HttpContext context, string org, string project, string stack, string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, NamespaceRoutingHelpers namespaceRoutingHelpers, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return Results.BadRequest(new ErrorResponse(Code: 400, Message: "invalid continuation token", RequestId: null));
            }

            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = await store.GetStackIdsByNamespaceIdAsync(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var view = await updateViewMaterializer.BuildAsync(ids.Value.StackId, updateId, afterSequence, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/renew_lease", async (HttpContext context, string updateId, RenewUpdateLeaseRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var token = endpointHelpers.GetUpdateToken(context) ?? string.Empty;
            var expiration = await store.RenewUpdateTokenLeaseAsync(updateId, token, request.Duration, cancellationToken);
            if (expiration is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new RenewUpdateLeaseResponse(token, expiration.Value));
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/renew_lease", async (HttpContext context, string updateId, RenewUpdateLeaseRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var token = endpointHelpers.GetUpdateToken(context) ?? string.Empty;
            var expiration = await store.RenewUpdateTokenLeaseAsync(updateId, token, request.Duration, cancellationToken);
            if (expiration is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new RenewUpdateLeaseResponse(token, expiration.Value));
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

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events/batch", async (HttpContext context, string org, string project, string stack, string updateId, ApiEngineEventBatch request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            if (request.Events.Any(static ev => !ev.HasSinglePayload))
            {
                return InvalidEngineEventResponse();
            }

            var eventsJson = request.Events.Select(SerializeEvent).ToArray();
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
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events/batch", async (HttpContext context, string org, string project, string stack, string updateId, ApiEngineEventBatch request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            if (request.Events.Any(static ev => !ev.HasSinglePayload))
            {
                return InvalidEngineEventResponse();
            }

            var eventsJson = request.Events.Select(SerializeEvent).ToArray();
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

        app.MapGet("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events", async (string updateId, string? continuationToken, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid continuation token", null));
            }

            var response = await ReadUpdateEventsResponseAsync(store, updateId, afterSequence, cancellationToken);
            return Results.Ok(response);
        });
        app.MapGet("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events", async (string updateId, string? continuationToken, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid continuation token", null));
            }

            var response = await ReadUpdateEventsResponseAsync(store, updateId, afterSequence, cancellationToken);
            return Results.Ok(response);
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events", async (HttpContext context, string org, string project, string stack, string updateId, EngineEvent request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            if (!request.HasSinglePayload)
            {
                return InvalidEngineEventResponse();
            }

            await store.RecordEventsAsync(updateId, [SerializeEvent(request)], cancellationToken);
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
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events", async (HttpContext context, string org, string project, string stack, string updateId, EngineEvent request, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            if (!request.HasSinglePayload)
            {
                return InvalidEngineEventResponse();
            }

            await store.RecordEventsAsync(updateId, [SerializeEvent(request)], cancellationToken);
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
            await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(updateId, cancellationToken);
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
            await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(updateId, cancellationToken);
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

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/complete", async (HttpContext context, string org, string project, string stack, string updateId, CompleteUpdateRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var isSucceeded = string.Equals(request.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
            if (!isSucceeded)
            {
                await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(updateId, cancellationToken);
            }

            await store.CompleteUpdateAsync(updateId, request.Status, cancellationToken: cancellationToken);
            if (isSucceeded)
            {
                var materialized = await checkpointJournalingProvider.FinalizeSucceededUpdateAsync(
                    updateId,
                    cancellationToken);
                var updateContext = await store.GetUpdatePersistenceContextAsync(updateId, cancellationToken);
                if (!materialized &&
                    updateContext is { Kind: "destroy" } &&
                    !endpointHelpers.IsPreviewProgramRequest(updateContext.Value.ProgramRequestJson))
                {
                    var latestDeployment = await store.GetLatestDeploymentAsync(
                        updateContext.Value.StackId,
                        cancellationToken);
                    var emptyDeployment = endpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                    await store.SaveCheckpointAsync(
                        updateId,
                        emptyDeployment,
                        updateContext.Value.Version,
                        cancellationToken);
                }
                else if (updateContext is { Kind: "destroy" } &&
                         !endpointHelpers.IsPreviewProgramRequest(updateContext.Value.ProgramRequestJson))
                {
                    var latestDeployment = await store.GetLatestDeploymentAsync(
                        updateContext.Value.StackId,
                        cancellationToken);
                    if (DeploymentHasManagedResources(latestDeployment?.DeploymentJson))
                    {
                        var emptyDeployment = endpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                        await store.SaveCheckpointAsync(
                            updateId,
                            emptyDeployment,
                            updateContext.Value.Version,
                            cancellationToken);
                    }
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
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/complete", async (HttpContext context, string org, string project, string stack, string updateId, CompleteUpdateRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            if (!await RequireInProgressUpdateAsync(store, updateId, cancellationToken))
            {
                return UpdateNotInProgressResponse();
            }

            var isSucceeded = string.Equals(request.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
            if (!isSucceeded)
            {
                await checkpointJournalingProvider.RecoverInterruptedUpdateAsync(updateId, cancellationToken);
            }

            await store.CompleteUpdateAsync(updateId, request.Status, cancellationToken: cancellationToken);
            if (isSucceeded)
            {
                var materialized = await checkpointJournalingProvider.FinalizeSucceededUpdateAsync(
                    updateId,
                    cancellationToken);
                var updateContext = await store.GetUpdatePersistenceContextAsync(updateId, cancellationToken);
                if (!materialized &&
                    updateContext is { Kind: "destroy" } &&
                    !endpointHelpers.IsPreviewProgramRequest(updateContext.Value.ProgramRequestJson))
                {
                    var latestDeployment = await store.GetLatestDeploymentAsync(
                        updateContext.Value.StackId,
                        cancellationToken);
                    var emptyDeployment = endpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                    await store.SaveCheckpointAsync(
                        updateId,
                        emptyDeployment,
                        updateContext.Value.Version,
                        cancellationToken);
                }
                else if (updateContext is { Kind: "destroy" } &&
                         !endpointHelpers.IsPreviewProgramRequest(updateContext.Value.ProgramRequestJson))
                {
                    var latestDeployment = await store.GetLatestDeploymentAsync(
                        updateContext.Value.StackId,
                        cancellationToken);
                    if (DeploymentHasManagedResources(latestDeployment?.DeploymentJson))
                    {
                        var emptyDeployment = endpointHelpers.CreateEmptyDeploymentJson(latestDeployment?.DeploymentJson);
                        await store.SaveCheckpointAsync(
                            updateId,
                            emptyDeployment,
                            updateContext.Value.Version,
                            cancellationToken);
                    }
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
