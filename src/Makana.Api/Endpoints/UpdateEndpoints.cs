using System.Security.Claims;
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
    public static IEndpointRouteBuilder MapUpdateEndpoints(
        this IEndpointRouteBuilder app,
        StackCatalogQueryRepository stackQueries,
        UpdateCommandRepository updateCommands,
        UpdateQueryRepository updateQueries,
        UpdateLogRepository updateLog,
        IStackUpdateNotifier updateNotifier,
        IUpdateCheckpointJournalingProvider checkpointJournalingProvider,
        EphemeralStateJanitor ephemeralStateJanitor,
        IdentityRepository identityRepository,
        CoreEndpoints.OidcSettings oidcSettings,
        OidcUserInfoClient oidcUserInfoClient)
    {
        static bool RequireInProgressUpdateAsync(
            UpdateCommandRepository updateCommands,
            string updateId)
        {
            return updateCommands.IsUpdateInProgress(updateId);
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

        async Task<(string? NamespaceId, IResult? Failure)> ResolveNamespaceIdForRouteAsync(
            HttpContext context, NamespaceRoutingHelpers namespaceRoutingHelpers, string org)
        {
            var authKind = context.User.FindFirst(PulumiAuthenticationDefaults.AuthKindClaim)?.Value;
            var userId = await ResolveUserIdAsync(context);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return (null, Results.Unauthorized());
            }

            var tokenOrgId = context.User.FindFirst(PulumiAuthenticationDefaults.OrgIdClaim)?.Value;
            if (string.Equals(authKind, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                var login = OidcClaimHelpers.ResolveLogin(context.User);
                if (!string.IsNullOrWhiteSpace(login) &&
                    string.Equals(login, org, StringComparison.OrdinalIgnoreCase))
                {
                    return (userId, null);
                }
            }

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

        async Task<string?> ResolveUserIdAsync(HttpContext context)
        {
            var authKind = context.User.FindFirst(PulumiAuthenticationDefaults.AuthKindClaim)?.Value;
            var userId = context.User.FindFirst(PulumiAuthenticationDefaults.UserIdClaim)?.Value;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                return userId;
            }

            if (!string.Equals(authKind, "bearer", StringComparison.OrdinalIgnoreCase))
            {
                return context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            }

            var principal = context.User;
            if (string.IsNullOrWhiteSpace(OidcClaimHelpers.ResolveLogin(principal)))
            {
                principal = await oidcUserInfoClient.TryEnrichPrincipalAsync(
                    context,
                    principal,
                    context.RequestAborted) ?? principal;
            }

            try
            {
                var resolvedUserId = await EnsureIdentityFromClaimsAsync(principal, identityRepository, oidcSettings.ProviderKey);
                if (principal.Identity is ClaimsIdentity identity)
                {
                    if (!identity.HasClaim(claim => claim.Type == PulumiAuthenticationDefaults.UserIdClaim))
                    {
                        identity.AddClaim(new Claim(PulumiAuthenticationDefaults.UserIdClaim, resolvedUserId));
                    }

                    var login = OidcClaimHelpers.ResolveLogin(principal);
                    if (!string.IsNullOrWhiteSpace(login) &&
                        !identity.HasClaim(claim => claim.Type == ClaimTypes.Name))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Name, login));
                    }
                }

                if (context.User.Identity is ClaimsIdentity contextIdentity &&
                    !contextIdentity.HasClaim(claim => claim.Type == PulumiAuthenticationDefaults.UserIdClaim))
                {
                    contextIdentity.AddClaim(new Claim(PulumiAuthenticationDefaults.UserIdClaim, resolvedUserId));
                }

                return resolvedUserId;
            }
            catch
            {
                return null;
            }
        }

        static Task<string> EnsureIdentityFromClaimsAsync(
            ClaimsPrincipal principal,
            IdentityRepository identityRepository,
            string providerKey)
        {
            var login = OidcClaimHelpers.ResolveLogin(principal)
                ?? throw new InvalidOperationException("Missing OIDC login claim.");
            var canonicalUserId = OidcClaimHelpers.ResolveCanonicalUserId(principal, providerKey)
                ?? throw new InvalidOperationException("Missing subject claim.");
            var userId = identityRepository.GetUserIdByLogin(login) ?? canonicalUserId;
            var displayName = GetClaim(principal, "name", ClaimTypes.Name) ?? login;
            var email = GetClaim(principal, "email", ClaimTypes.Email);
            var avatar = GetClaim(principal, "picture") ?? string.Empty;

            identityRepository.EnsureUser(userId, login, displayName, email, avatar);
            return Task.FromResult(userId);
        }

        static string? GetClaim(ClaimsPrincipal principal, params string[] claimTypes)
        {
            foreach (var claimType in claimTypes)
            {
                var claim = principal.FindFirst(claimType)?.Value;
                if (!string.IsNullOrWhiteSpace(claim))
                {
                    return claim;
                }
            }

            return null;
        }

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates", async (HttpContext context, string org, string project, string stack, bool? includePreviews, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var stackIds = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            if (stackIds is null)
            {
                return Results.NotFound();
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
            return Results.Ok(new GetHistoryResponse(updateInfos));
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/latest", async (HttpContext context, string org, string project, string stack, [FromQuery(Name = "output-type")] string? outputType, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers, VersionedUpdatePageMaterializer versionedUpdatePageMaterializer, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var config = stackQueries.GetStackConfigByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            var latest = updateQueries.GetHistory(ids.Value.StackId, includePreviews: true).FirstOrDefault();
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

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var update = updateQueries.GetUpdateByVersion(ids.Value.StackId, version);
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

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var update = updateQueries.GetUpdateByVersion(ids.Value.StackId, version);
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

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var summary = await versionedUpdatePageMaterializer.BuildVersionSummaryAsync(ids.Value.StackId, version, cancellationToken);
            return summary is null ? Results.NotFound() : Results.Ok(summary);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/updates/{version}/contents/files", (string org, string project, string stack, int version) =>
        {
            return Results.Ok(new UpdateContentsFilesResponse([]));
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
                stackQueries, updateCommands, namespaceResolution.NamespaceId!, project, stack, "update", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/preview", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await endpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                stackQueries, updateCommands, namespaceResolution.NamespaceId!, project, stack, "preview", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/refresh", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await endpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                stackQueries, updateCommands, namespaceResolution.NamespaceId!, project, stack, "refresh", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/destroy", async (HttpContext context, string org, string project, string stack, UpdateProgramRequest request, EndpointHelpers endpointHelpers, NamespaceRoutingHelpers namespaceRoutingHelpers) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            return await endpointHelpers.CreateUpdateResponseByNamespaceIdAsync(
                stackQueries, updateCommands, namespaceResolution.NamespaceId!, project, stack, "destroy", request);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}", async (HttpContext context, string org, string project, string stack, string updateId, StartUpdateRequest request, NamespaceRoutingHelpers namespaceRoutingHelpers, CancellationToken cancellationToken) =>
        {
            var namespaceResolution = await ResolveNamespaceIdForRouteAsync(context, namespaceRoutingHelpers, org);
            if (namespaceResolution.Failure is not null)
            {
                return namespaceResolution.Failure;
            }

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            await ephemeralStateJanitor.CancelExpiredUpdatesForStackAsync(ids.Value.StackId, cancellationToken);
            var started = updateCommands.StartUpdate(
                updateId,
                ids.Value.StackId,
                JsonSerializer.Serialize(request, UpdateEndpointsJsonContext.Default.StartUpdateRequest));
            if (started.Status == UpdateStartStatus.NotFound)
            {
                return Results.NotFound();
            }

            if (started.Status == UpdateStartStatus.Conflict)
            {
                return Results.Conflict(new ErrorResponse(409, "another update is already in progress", null));
            }

            if (started.Status != UpdateStartStatus.Started || string.IsNullOrWhiteSpace(started.Token))
            {
                return Results.Conflict(new ErrorResponse(409, "update is not in a startable state", null));
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

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            await ephemeralStateJanitor.CancelExpiredUpdatesForStackAsync(ids.Value.StackId, cancellationToken);
            var started = updateCommands.StartUpdate(
                updateId,
                ids.Value.StackId,
                JsonSerializer.Serialize(request, UpdateEndpointsJsonContext.Default.StartUpdateRequest));
            if (started.Status == UpdateStartStatus.NotFound)
            {
                return Results.NotFound();
            }

            if (started.Status == UpdateStartStatus.Conflict)
            {
                return Results.Conflict(new ErrorResponse(409, "another update is already in progress", null));
            }

            if (started.Status != UpdateStartStatus.Started || string.IsNullOrWhiteSpace(started.Token))
            {
                return Results.Conflict(new ErrorResponse(409, "update is not in a startable state", null));
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
            return Results.Ok(new StartUpdateResponse(started.Version, started.Token, journalVersion));
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/update/{updateId}", async (string updateId, string? continuationToken, UpdateViewMaterializer updateViewMaterializer, CancellationToken cancellationToken) =>
        {
            var updateContext = updateCommands.GetUpdateContext(updateId);
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
            var updateContext = updateCommands.GetUpdateContext(updateId);
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

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
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

            var ids = stackQueries.GetStackIdsByNamespaceId(namespaceResolution.NamespaceId!, project, stack);
            if (ids is null)
            {
                return Results.NotFound();
            }

            var view = await updateViewMaterializer.BuildAsync(ids.Value.StackId, updateId, afterSequence, cancellationToken);
            return view is null ? Results.NotFound() : Results.Ok(view);
        }).RequireAuthorization("OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/renew_lease", async (HttpContext context, string updateId, RenewUpdateLeaseRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var token = endpointHelpers.GetUpdateToken(context) ?? string.Empty;
            var expiration = updateCommands.RenewUpdateTokenLease(updateId, token, request.Duration);
            if (expiration is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new RenewUpdateLeaseResponse(token, expiration.Value));
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/renew_lease", async (HttpContext context, string updateId, RenewUpdateLeaseRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
            {
                return UpdateNotInProgressResponse();
            }

            var token = endpointHelpers.GetUpdateToken(context) ?? string.Empty;
            var expiration = updateCommands.RenewUpdateTokenLease(updateId, token, request.Duration);
            if (expiration is null)
            {
                return Results.NotFound();
            }

            return Results.Ok(new RenewUpdateLeaseResponse(token, expiration.Value));
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPatch("/api/stacks/{org}/{project}/{stack}/update/{updateId}/checkpoint", async (HttpContext context, string updateId, PatchUpdateCheckpointRequest request, CancellationToken cancellationToken) =>
        {
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
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
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
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
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
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
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
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
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
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
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
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
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
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
            if (!RequireInProgressUpdateAsync(updateCommands, updateId))
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
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events/batch", async (HttpContext context, string org, string project, string stack, string updateId, ApiEngineEventBatch request, CancellationToken cancellationToken) =>
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
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapGet("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events", async (string updateId, string? continuationToken, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid continuation token", null));
            }

            var response = await ReadUpdateEventsResponseAsync(updateCommands, updateLog, updateId, afterSequence, cancellationToken);
            return Results.Ok(response);
        });
        app.MapGet("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events", async (string updateId, string? continuationToken, CancellationToken cancellationToken) =>
        {
            if (!TryParseContinuationToken(continuationToken, out var afterSequence))
            {
                return Results.BadRequest(new ErrorResponse(400, "invalid continuation token", null));
            }

            var response = await ReadUpdateEventsResponseAsync(updateCommands, updateLog, updateId, afterSequence, cancellationToken);
            return Results.Ok(response);
        });

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/events", async (HttpContext context, string org, string project, string stack, string updateId, EngineEvent request, CancellationToken cancellationToken) =>
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
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/events", async (HttpContext context, string org, string project, string stack, string updateId, EngineEvent request, CancellationToken cancellationToken) =>
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
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/cancel", async (HttpContext context, string org, string project, string stack, string updateId, CancellationToken cancellationToken) =>
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
            return Results.Ok();
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/cancel", async (HttpContext context, string org, string project, string stack, string updateId, CancellationToken cancellationToken) =>
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
            return Results.Ok();
        })
            .RequireAuthorization("ApiToken", "OrgRouteAccess");

        app.MapPost("/api/stacks/{org}/{project}/{stack}/update/{updateId}/complete", async (HttpContext context, string org, string project, string stack, string updateId, CompleteUpdateRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
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
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");
        app.MapPost("/api/stacks/{org}/{project}/{stack}/{updateKind}/{updateId}/complete", async (HttpContext context, string org, string project, string stack, string updateId, CompleteUpdateRequest request, EndpointHelpers endpointHelpers, CancellationToken cancellationToken) =>
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
            return Results.Ok();
        })
            .RequireAuthorization("UpdateTokenForRoute");

        return app;
    }
}
