using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Makana.Api.Contracts;
using Makana.Api.Endpoints;
using Makana.Api.Storage;
using Makana.StackUpdates.Models;
using Makana.StackUpdates.Pulumi;
using Makana.StackUpdates.Services;

namespace Makana.Api.Updates;

internal sealed class UpdateViewMaterializer(
    BackendStore store,
    EndpointHelpers endpointHelpers,
    StackUpdateTranscriptMapper transcriptMapper,
    UpdateProjectionReducer projectionReducer,
    StackTranscriptDeltaBuilder transcriptDeltaBuilder,
    StackConsoleDeltaBuilder consoleDeltaBuilder,
    UpdateProjectionStateCache projectionStateCache)
{
    public async Task<RecoveredUpdateState?> BuildRecoveredStateAsync(
        string stackId,
        string updateId,
        CancellationToken cancellationToken)
    {
        var update = await store.GetUpdateDetailsAsync(stackId, updateId, cancellationToken);
        if (update is null)
        {
            return null;
        }

        var projectionState = await GetLatestProjectionStateAsync(updateId, cancellationToken);
        return await BuildRecoveredStateFromProjectionAsync(update.Value, projectionState.BuildProjection(), cancellationToken);
    }

    public async Task<UpdateViewResponse?> BuildAsync(
        string stackId,
        string updateId,
        long? afterSequence,
        CancellationToken cancellationToken)
    {
        var update = await store.GetUpdateDetailsAsync(stackId, updateId, cancellationToken);
        if (update is null)
        {
            return null;
        }

        var previousState = afterSequence.HasValue
            ? await GetProjectionStateAtSequenceAsync(updateId, afterSequence.Value, cancellationToken)
            : null;
        var currentState = afterSequence.HasValue
            ? await AdvanceProjectionStateAsync(updateId, afterSequence.Value, previousState!, cancellationToken)
            : await GetLatestProjectionStateAsync(updateId, cancellationToken);
        var recoveredState = await BuildRecoveredStateFromProjectionAsync(update.Value, currentState.BuildProjection(), cancellationToken);
        var stack = endpointHelpers.BuildStackPageMetadata(
            await store.GetStackPageMetadataByIdAsync(stackId, cancellationToken));
        var header = BuildHeader(recoveredState);
        var history = await BuildHistoryAsync(stackId, updateId, cancellationToken);

        var changeEntries = BuildChangeEntries(recoveredState.TranscriptRows);
        var previousRows = previousState is null
            ? []
            : transcriptMapper.Map(previousState.BuildProjection()).ToArray();
        var transcriptDeltas = afterSequence.HasValue
            ? transcriptDeltaBuilder.Build(previousRows, recoveredState.TranscriptRows)
            : [];
        var deltaEntries = BuildChangeEntries(transcriptDeltas);
        var summaryLines = StackUpdateTextFormatter.BuildSummaryLineProjections(recoveredState.TranscriptRows);
        var diffLines = StackUpdateTextFormatter.BuildDiffLineProjections(recoveredState.TranscriptRows);
        var diagnosticLines = StackUpdateTextFormatter.BuildDiagnosticLineProjections(recoveredState.TranscriptRows, string.Empty, string.Empty, string.Empty);
        var summaryText = string.Join('\n', summaryLines.Select(static line => line.Text));
        var diffText = string.Join('\n', diffLines.Select(static line => line.Text));
        var diagnosticText = string.Join('\n', diagnosticLines.Select(static line => line.Text));
        var previousSummaryLines = afterSequence.HasValue
            ? StackUpdateTextFormatter.BuildSummaryLineProjections(previousRows)
            : [];
        var previousDiffLines = afterSequence.HasValue
            ? StackUpdateTextFormatter.BuildDiffLineProjections(previousRows)
            : [];
        var previousDiagnosticLines = afterSequence.HasValue
            ? StackUpdateTextFormatter.BuildDiagnosticLineProjections(previousRows, string.Empty, string.Empty, string.Empty)
            : [];
        var summaryLineDeltas = afterSequence.HasValue
            ? BuildConsoleLineDeltas(StackConsoleSemanticRenderer.RenderLineDeltas(
                StackConsoleViewKind.Summary,
                consoleDeltaBuilder.Build(previousSummaryLines, summaryLines)))
            : [];
        var diffLineDeltas = afterSequence.HasValue
            ? BuildConsoleLineDeltas(StackConsoleSemanticRenderer.RenderLineDeltas(
                StackConsoleViewKind.Diff,
                consoleDeltaBuilder.Build(previousDiffLines, diffLines)))
            : [];
        var diagnosticLineDeltas = afterSequence.HasValue
            ? BuildConsoleLineDeltas(StackConsoleSemanticRenderer.RenderLineDeltas(
                StackConsoleViewKind.Diagnostic,
                consoleDeltaBuilder.Build(previousDiagnosticLines, diagnosticLines)))
            : [];
        var renderedSummary = StackConsoleSemanticRenderer.Render(StackConsoleViewKind.Summary, summaryLines, summaryText);
        var renderedDiff = StackConsoleSemanticRenderer.Render(StackConsoleViewKind.Diff, diffLines, diffText);
        var renderedDiagnostic = StackConsoleSemanticRenderer.Render(StackConsoleViewKind.Diagnostic, diagnosticLines, diagnosticText);
        var includeFullChanges = !afterSequence.HasValue || afterSequence.Value == 0 || !recoveredState.IsInProgress;
        var changes = new UpdateViewChanges(
            HasEvents: recoveredState.TranscriptRows.Length > 0,
            MessageCount: recoveredState.TranscriptRows.OfType<StackConfigurationTranscriptRow>().Count() +
                          recoveredState.TranscriptRows.OfType<StackDiagnosticTranscriptRow>().Count(),
            SummaryText: includeFullChanges ? summaryText : string.Empty,
            DiffText: includeFullChanges ? diffText : string.Empty,
            DiagnosticText: includeFullChanges ? diagnosticText : string.Empty,
            Entries: includeFullChanges ? changeEntries : [],
            DeltaEntries: deltaEntries,
            SummaryLines: includeFullChanges ? BuildConsoleLines(renderedSummary.Lines) : [],
            DiffLines: includeFullChanges ? BuildConsoleLines(renderedDiff.Lines) : [],
            DiagnosticLines: includeFullChanges ? BuildConsoleLines(renderedDiagnostic.Lines) : [],
            SummaryLineDeltas: summaryLineDeltas,
            DiffLineDeltas: diffLineDeltas,
            DiagnosticLineDeltas: diagnosticLineDeltas);
        var delta = BuildDelta(afterSequence, transcriptDeltas, previousRows, recoveredState.TranscriptRows, recoveredState.IsInProgress);

        return new UpdateViewResponse(
            stack,
            recoveredState.Info,
            header,
            history,
            recoveredState.Configuration,
            recoveredState.Outputs,
            recoveredState.Resources,
            recoveredState.Summary,
            changes,
            recoveredState.ContinuationToken,
            delta);
    }

    private async Task<UpdateViewHistoryEntry[]> BuildHistoryAsync(
        string stackId,
        string selectedUpdateId,
        CancellationToken cancellationToken)
    {
        var updates = await store.GetHistoryAsync(stackId, includePreviews: true);
        if (updates.Count == 0)
        {
            return [];
        }

        var previewFlags = await store.GetUpdatePreviewFlagsAsync(updates.Select(static update => update.Id).ToArray(), cancellationToken);
        return updates.Select(update =>
            {
                var isPreview = endpointHelpers.IsPreviewOperation(
                    update.Kind,
                    update.ProgramRequestJson,
                    previewFlags.GetValueOrDefault(update.Id));
                var (message, _) = endpointHelpers.ExtractMetadata(update.ProgramRequestJson);
                return new UpdateViewHistoryEntry(
                    UpdateId: update.Id,
                    Version: update.Version,
                    Kind: update.Kind,
                    IsPreview: isPreview,
                    Title: endpointHelpers.FormatUpdateHistoryTitle(update.Version),
                    KindLabel: endpointHelpers.FormatUpdateKindLabel(update.Kind, isPreview),
                    RunLabel: endpointHelpers.FormatUpdateRunLabel(update.Kind, isPreview, update.Version),
                    Status: endpointHelpers.NormalizeUpdateStatus(update.Status),
                    StartTime: endpointHelpers.ToUnixTimeSeconds(update.CreatedAt),
                    Message: message,
                    RouteRunType: endpointHelpers.GetUpdateRouteRunType(update.Kind, isPreview),
                    RouteReference: endpointHelpers.GetUpdateRouteReference(update.Id, update.Version, update.Kind, isPreview),
                    IsSelected: string.Equals(update.Id, selectedUpdateId, StringComparison.Ordinal));
            })
            .ToArray();
    }

    private UpdateViewHeader BuildHeader(RecoveredUpdateState recoveredState)
    {
        var info = recoveredState.Info;
        var isPreview = recoveredState.Summary?.IsPreview
            ?? endpointHelpers.IsPreviewOperation(info.Kind, null);
        int? resourceCount = recoveredState.Resources.Length > 0 ? recoveredState.Resources.Length : null;

        return new UpdateViewHeader(
            Kind: info.Kind,
            IsPreview: isPreview,
            KindLabel: endpointHelpers.FormatUpdateKindLabel(info.Kind, isPreview),
            RunLabel: endpointHelpers.FormatUpdateRunLabel(info.Kind, isPreview, info.Version),
            Status: info.Status ?? string.Empty,
            Message: info.Message,
            StartTime: info.StartTime,
            EndTime: info.EndTime,
            ResourceCount: resourceCount,
            ResourceChanges: recoveredState.Summary?.ResourceChanges);
    }

    private async Task<RecoveredUpdateState> BuildRecoveredStateFromProjectionAsync(
        (string Id, string StackId, string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson) update,
        UpdateProjection projection,
        CancellationToken cancellationToken)
    {
        var latestCheckpoint = await store.GetLatestCheckpointForUpdateAsync(update.Id, cancellationToken);
        var fallbackDeploymentJson = latestCheckpoint?.DeploymentJson;
        if (string.IsNullOrWhiteSpace(fallbackDeploymentJson))
        {
            var latestDeployment = await store.GetLatestDeploymentAsync(update.StackId, cancellationToken);
            fallbackDeploymentJson = latestDeployment?.DeploymentJson;
        }

        var (message, _) = endpointHelpers.ExtractMetadata(update.ProgramRequestJson);
        var configuration = BuildConfiguration(update.ProgramRequestJson);
        if (configuration.Count == 0 && projection.Config is not null)
        {
            configuration.AddRange(projection.Config.Entries.Select(static entry =>
                new UpdateViewConfigEntry(entry.Key, entry.Value, false, false)));
        }

        var resources = projection.Resources
            .Select(static resource => new MutableResource(
                resource.ResourceUrn,
                resource.ParentUrn,
                resource.ResourceType,
                resource.ResourceName,
                resource.Operation,
                resource.Status ?? string.Empty,
                resource.Info,
                resource.IsError))
            .ToList();
        Dictionary<string, string?>? oldOutputs = projection.StackOutputs?.OldOutputs is null
            ? null
            : new Dictionary<string, string?>(projection.StackOutputs.OldOutputs, StringComparer.Ordinal);
        Dictionary<string, string?>? newOutputs = projection.StackOutputs?.NewOutputs is null
            ? null
            : new Dictionary<string, string?>(projection.StackOutputs.NewOutputs, StringComparer.Ordinal);
        UpdateViewSummary? summary = projection.Summary is null
            ? null
            : new UpdateViewSummary(
                projection.Summary.Event.IsPreview,
                projection.Summary.Event.MaybeCorrupt,
                projection.Summary.Event.DurationSeconds,
                projection.Summary.Event.ResourceChanges.Count == 0
                    ? null
                    : new Dictionary<string, int>(projection.Summary.Event.ResourceChanges, StringComparer.Ordinal));

        ApplyCheckpointFallback(fallbackDeploymentJson, resources, ref newOutputs);

        var transcriptRows = transcriptMapper.Map(projection);
        if (transcriptRows.Count == 0 && resources.Count > 0)
        {
            transcriptRows = BuildCheckpointTranscript(resources, newOutputs);
        }

        summary ??= BuildSummaryFallback(resources);

        long? latestSequence = projection.LastSequence >= 0 ? projection.LastSequence : null;
        var isInProgress = string.Equals(update.Status, "in-progress", StringComparison.Ordinal);
        var continuationToken = isInProgress
            ? (latestSequence ?? 0).ToString(CultureInfo.InvariantCulture)
            : null;

        var outputs = BuildOutputs(oldOutputs, newOutputs);
        var info = new UpdateViewInfo(
            Kind: update.Kind,
            Status: endpointHelpers.NormalizeUpdateStatus(update.Status),
            Version: update.Version,
            Message: message,
            StartTime: endpointHelpers.ToUnixTimeSeconds(update.CreatedAt),
            EndTime: endpointHelpers.ToUnixTimeSeconds(update.CompletedAt),
            UpdateId: update.Id);

        return new RecoveredUpdateState(
            info,
            isInProgress,
            continuationToken,
            latestSequence,
            configuration.OrderBy(static entry => entry.Key, StringComparer.Ordinal).ToArray(),
            outputs,
            resources.Select(static resource => resource.ToContract()).ToArray(),
            summary,
            transcriptRows.ToArray());
    }

    private static UpdateViewConsoleLine[] BuildConsoleLines(IReadOnlyList<StackConsoleRenderedLine> lines)
        => lines.Select(static line => new UpdateViewConsoleLine(
                line.Key,
                line.Text,
                line.CssClass,
                line.Segments.Select(static segment => new UpdateViewConsoleSegment(segment.Text, segment.CssClass)).ToArray()))
            .ToArray();

    private static UpdateViewConsoleLineDelta[] BuildConsoleLineDeltas(IReadOnlyList<StackConsoleRenderedDelta> deltas)
        => deltas.Select(static delta => new UpdateViewConsoleLineDelta(
                delta.Key,
                delta.Action,
                delta.Line is null
                    ? null
                    : new UpdateViewConsoleLine(
                        delta.Line.Key,
                        delta.Line.Text,
                        delta.Line.CssClass,
                        delta.Line.Segments.Select(static segment => new UpdateViewConsoleSegment(segment.Text, segment.CssClass)).ToArray())))
            .ToArray();

    private async Task<UpdateProjectionReducer.ProjectionState> GetLatestProjectionStateAsync(
        string updateId,
        CancellationToken cancellationToken)
    {
        var latestSequence = await store.GetLatestEventSequenceAsync(updateId, cancellationToken);
        if (latestSequence is > 0 && projectionStateCache.TryGet(updateId, latestSequence.Value, out var cached))
        {
            return cached!;
        }

        var events = await store.GetEventsAsync(updateId, cancellationToken);
        var parsedEvents = DeserializeStoredEvents(events);
        var state = projectionReducer.ReplayState(parsedEvents);
        if (state.Sequence >= 0)
        {
            projectionStateCache.Set(updateId, state.Sequence, state);
        }

        return state;
    }

    private async Task<UpdateProjectionReducer.ProjectionState> GetProjectionStateAtSequenceAsync(
        string updateId,
        long sequence,
        CancellationToken cancellationToken)
    {
        if (sequence <= 0)
        {
            return new UpdateProjectionReducer.ProjectionState();
        }

        if (projectionStateCache.TryGet(updateId, sequence, out var cached))
        {
            return cached!;
        }

        var events = await store.GetEventsAsync(updateId, cancellationToken);
        var parsedEvents = DeserializeStoredEvents(events);
        var state = projectionReducer.ReplayStateUntil(parsedEvents, sequence);
        if (state.Sequence >= 0)
        {
            projectionStateCache.Set(updateId, state.Sequence, state);
        }

        return state;
    }

    private async Task<UpdateProjectionReducer.ProjectionState> AdvanceProjectionStateAsync(
        string updateId,
        long afterSequence,
        UpdateProjectionReducer.ProjectionState previousState,
        CancellationToken cancellationToken)
    {
        var latestSequence = await store.GetLatestEventSequenceAsync(updateId, cancellationToken);
        if (latestSequence is null || latestSequence <= afterSequence)
        {
            return previousState.Clone();
        }

        if (projectionStateCache.TryGet(updateId, latestSequence.Value, out var cached))
        {
            return cached!;
        }

        var state = previousState.Clone();
        var newEvents = await LoadEventsAfterSequenceAsync(updateId, afterSequence, cancellationToken);
        projectionReducer.ApplyRange(state, newEvents);
        if (state.Sequence >= 0)
        {
            projectionStateCache.Set(updateId, state.Sequence, state);
        }

        return state;
    }

    private async Task<IReadOnlyList<EngineEvent>> LoadEventsAfterSequenceAsync(
        string updateId,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        const int pageSize = 1024;
        var events = new List<EngineEvent>();
        long? cursor = afterSequence;

        while (true)
        {
            var page = await store.GetEventPageAsync(updateId, cursor, pageSize, cancellationToken);
            if (page.EventsJson.Count == 0)
            {
                break;
            }

            events.AddRange(DeserializeStoredEvents(page.EventsJson));
            if (page.LastSequence is null || page.EventsJson.Count < pageSize)
            {
                break;
            }

            cursor = page.LastSequence.Value;
        }

        return events;
    }

    private static EngineEvent[] DeserializeStoredEvents(IReadOnlyList<string> eventsJson)
    {
        var events = new List<EngineEvent>(eventsJson.Count);
        foreach (var eventJson in eventsJson)
        {
            var envelope = JsonSerializer.Deserialize(eventJson, PulumiEngineEventJsonContext.Default.EngineEvent);
            if (envelope is null || !envelope.HasSinglePayload)
            {
                continue;
            }

            events.Add(envelope);
        }

        return [.. events];
    }

    private List<UpdateViewConfigEntry> BuildConfiguration(string? programRequestJson)
    {
        var config = endpointHelpers.ExtractConfig(programRequestJson);
        if (config is null || config.Count == 0)
        {
            return [];
        }

        return config.Select(static entry => new UpdateViewConfigEntry(
                entry.Key,
                entry.Value.Secret ? "[secret]" : entry.Value.String,
                entry.Value.Secret,
                entry.Value.Object))
            .ToList();
    }

    private static bool TryReadPreludeConfig(EngineEvent envelope, out List<UpdateViewConfigEntry> entries)
    {
        entries = [];
        if (envelope.PreludeEvent?.Config is not { Count: > 0 } config)
        {
            return false;
        }

        foreach (var property in config)
        {
            entries.Add(new UpdateViewConfigEntry(property.Key, property.Value, false, false));
        }

        return entries.Count > 0;
    }

    private static bool TryApplyResourcePre(
        EngineEvent envelope,
        List<MutableResource> resources,
        Dictionary<string, int> resourcesByIdentity)
    {
        if (envelope.ResourcePreEvent?.Metadata is not { } metadata)
        {
            return false;
        }

        var operation = metadata.Op ?? "update";
        var urn = metadata.Urn;
        var identity = ResourceIdentity(urn);
        var parentUrn = metadata.New?.Parent ?? metadata.Old?.Parent;
        var resource = new MutableResource(
            urn,
            parentUrn,
            metadata.Type ?? TryExtractResourceType(urn),
            TryExtractResourceName(urn),
            operation,
            PresentTenseForOperation(operation),
            null,
            IsFailureOperation(operation));

        resourcesByIdentity[identity] = resources.Count;
        resources.Add(resource);
        return true;
    }

    private static bool TryApplyResourceOutputs(
        EngineEvent envelope,
        List<MutableResource> resources,
        Dictionary<string, int> resourcesByIdentity,
        ref Dictionary<string, string?>? oldOutputs,
        ref Dictionary<string, string?>? newOutputs)
    {
        if (envelope.ResOutputsEvent?.Metadata is not { } metadata)
        {
            return false;
        }

        var operation = metadata.Op ?? "same";
        var urn = metadata.Urn;
        var identity = ResourceIdentity(urn);
        var resource = new MutableResource(
            urn,
            metadata.New?.Parent ?? metadata.Old?.Parent,
            metadata.Type ?? TryExtractResourceType(urn),
            TryExtractResourceName(urn),
            operation,
            PastTenseForOperation(operation),
            FormatResourceInfo(metadata),
            IsFailureOperation(operation));

        if (resourcesByIdentity.TryGetValue(identity, out var existingIndex))
        {
            resources[existingIndex] = resource;
        }
        else
        {
            resources.Add(resource);
        }

        if (string.Equals(resource.Type, "pulumi:pulumi:Stack", StringComparison.Ordinal))
        {
            oldOutputs = ReadOutputs(metadata, "old");
            newOutputs = ReadOutputs(metadata, "new");
        }

        return true;
    }

    private static bool TryApplyResourceFailure(
        EngineEvent envelope,
        List<MutableResource> resources,
        Dictionary<string, int> resourcesByIdentity)
    {
        if (envelope.ResOpFailedEvent?.Metadata is not { } metadata)
        {
            return false;
        }

        var operation = metadata.Op ?? "update";
        var urn = metadata.Urn;
        var identity = ResourceIdentity(urn);
        var resource = new MutableResource(
            urn,
            metadata.New?.Parent ?? metadata.Old?.Parent,
            metadata.Type ?? TryExtractResourceType(urn),
            TryExtractResourceName(urn),
            operation,
            $"{PresentTenseForOperation(operation)} failed",
            FormatResourceInfo(metadata),
            true);

        if (resourcesByIdentity.TryGetValue(identity, out var existingIndex))
        {
            resources[existingIndex] = resource;
        }
        else
        {
            resources.Add(resource);
        }

        return true;
    }

    private static bool TryReadSummary(EngineEvent envelope, out UpdateViewSummary summary)
    {
        summary = default!;
        if (envelope.SummaryEvent is not { } summaryEvent)
        {
            return false;
        }

        summary = new UpdateViewSummary(
            IsPreview: summaryEvent.IsPreview,
            MaybeCorrupt: summaryEvent.MaybeCorrupt,
            DurationSeconds: summaryEvent.DurationSeconds,
            ResourceChanges: summaryEvent.ResourceChanges.Count == 0
                ? null
                : new Dictionary<string, int>(summaryEvent.ResourceChanges, StringComparer.Ordinal));
        return true;
    }

    private static UpdateViewOutputEntry[] BuildOutputs(
        Dictionary<string, string?>? oldOutputs,
        Dictionary<string, string?>? newOutputs)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (oldOutputs is not null)
        {
            foreach (var key in oldOutputs.Keys)
            {
                names.Add(key);
            }
        }

        if (newOutputs is not null)
        {
            foreach (var key in newOutputs.Keys)
            {
                names.Add(key);
            }
        }

        return names.Select(name =>
            {
                string? oldValue = null;
                string? newValue = null;
                oldOutputs?.TryGetValue(name, out oldValue);
                newOutputs?.TryGetValue(name, out newValue);
                return new UpdateViewOutputEntry(
                    name,
                    oldValue,
                    newValue,
                    !string.Equals(oldValue, newValue, StringComparison.Ordinal));
            })
            .ToArray();
    }

    private static Dictionary<string, string?>? ReadOutputs(StepEventMetadata metadata, string stateName)
    {
        var outputs = string.Equals(stateName, "old", StringComparison.Ordinal)
            ? metadata.Old?.Outputs
            : metadata.New?.Outputs;
        if (outputs is not { Count: > 0 })
        {
            return null;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var property in outputs)
        {
            values[property.Key] = JsonValueToInlineString(property.Value);
        }

        return values;
    }

    private static string ResourceIdentity(string? urn)
        => urn ?? string.Empty;

    private static string? FormatResourceInfo(StepEventMetadata metadata)
    {
        var fragments = new List<string>();
        if (metadata.DetailedDiff is { Count: > 0 } detailedDiff)
        {
            fragments.Add($"diff {detailedDiff.Count}");
        }

        return fragments.Count == 0 ? null : string.Join(" · ", fragments);
    }

    private static string? TryExtractResourceType(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return null;
        }

        var nameIndex = urn.LastIndexOf("::", StringComparison.Ordinal);
        if (nameIndex < 0)
        {
            return null;
        }

        var beforeName = urn[..nameIndex];
        var typeIndex = beforeName.LastIndexOf("::", StringComparison.Ordinal);
        return typeIndex < 0 ? null : beforeName[(typeIndex + 2)..];
    }

    private static string? TryExtractResourceName(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return null;
        }

        var index = urn.LastIndexOf("::", StringComparison.Ordinal);
        return index < 0 ? urn : urn[(index + 2)..];
    }

    private static string PresentTenseForOperation(string operation)
        => operation.ToLowerInvariant() switch
        {
            "create" => "creating",
            "create-replacement" => "creating replacement",
            "delete" => "deleting",
            "delete-replaced" => "deleting replaced",
            "replace" => "replacing",
            "read" => "reading",
            "read-replacement" => "reading replacement",
            "same" => "same",
            "update" => "updating",
            "import" => "importing",
            _ => operation.ToLowerInvariant()
        };

    private static string PastTenseForOperation(string operation)
        => operation.ToLowerInvariant() switch
        {
            "create" => "created",
            "create-replacement" => "created replacement",
            "delete" => "deleted",
            "delete-replaced" => "deleted replaced",
            "replace" => "replaced",
            "read" => "read",
            "read-replacement" => "read replacement",
            "same" => "same",
            "update" => "updated",
            "import" => "imported",
            _ => operation.ToLowerInvariant()
        };

    private static bool IsFailureOperation(string operation)
        => operation.Contains("fail", StringComparison.OrdinalIgnoreCase);

    private static string JsonValueToInlineString(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };

    private static void ApplyCheckpointFallback(
        string? deploymentJson,
        List<MutableResource> resources,
        ref Dictionary<string, string?>? newOutputs)
    {
        if (string.IsNullOrWhiteSpace(deploymentJson))
        {
            return;
        }

        DeploymentViewSnapshot snapshot;
        try
        {
            snapshot = ParseDeploymentViewSnapshot(deploymentJson);
        }
        catch (JsonException)
        {
            return;
        }

        if (resources.Count == 0 && snapshot.Resources.Count > 0)
        {
            resources.AddRange(snapshot.Resources);
        }

        if (newOutputs is null && snapshot.StackOutputs is { Count: > 0 })
        {
            newOutputs = snapshot.StackOutputs;
        }
    }

    private static DeploymentViewSnapshot ParseDeploymentViewSnapshot(string deploymentJson)
    {
        using var document = JsonDocument.Parse(deploymentJson);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("deployment", out var deploymentElement) &&
            deploymentElement.ValueKind == JsonValueKind.Object)
        {
            root = deploymentElement;
        }

        var resources = new List<MutableResource>();
        Dictionary<string, string?>? stackOutputs = null;

        if (root.TryGetProperty("resources", out var resourcesElement) &&
            resourcesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var resourceElement in resourcesElement.EnumerateArray())
            {
                var urn = TryGetStringProperty(resourceElement, "urn");
                var type = TryGetStringProperty(resourceElement, "type") ?? TryExtractResourceType(urn);
                var name = TryExtractResourceName(urn);
                var parentUrn = TryGetStringProperty(resourceElement, "parent");
                resources.Add(new MutableResource(
                    urn,
                    parentUrn,
                    type,
                    name,
                    "checkpoint",
                    "checkpoint",
                    null,
                    false));

                if (stackOutputs is null &&
                    string.Equals(type, "pulumi:pulumi:Stack", StringComparison.Ordinal) &&
                    resourceElement.TryGetProperty("outputs", out var outputsElement) &&
                    outputsElement.ValueKind == JsonValueKind.Object)
                {
                    stackOutputs = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (var property in outputsElement.EnumerateObject())
                    {
                        stackOutputs[property.Name] = JsonValueToInlineString(property.Value);
                    }
                }
            }
        }

        return new DeploymentViewSnapshot(resources, stackOutputs);
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static UpdateViewChangeEntry[] BuildChangeEntries(IEnumerable<StackTranscriptRow> rows)
        => rows.Select(MapChangeEntry).ToArray();

    private static UpdateViewChangeEntry[] BuildChangeEntries(IEnumerable<StackTranscriptRowDelta> deltas)
        => deltas.Select(MapChangeEntry).ToArray();

    private static UpdateViewChangeEntry MapChangeEntry(StackTranscriptRow row)
    {
        var action = row.UpdateKind.ToString().ToLowerInvariant();
        var kind = row.Kind.ToString().ToLowerInvariant();

        return row switch
        {
            StackConfigurationTranscriptRow configuration => new UpdateViewChangeEntry(
                configuration.Key,
                kind,
                configuration.Sequence,
                action,
                configuration.IsError,
                ConfigEntries: configuration.Entries
                    .Select(static entry => new UpdateViewChangeConfigEntry(entry.Key, entry.Value))
                    .ToArray(),
                RawPayloadJson: configuration.RawPayloadJson),
            StackResourceTranscriptRow resource => new UpdateViewChangeEntry(
                resource.Key,
                kind,
                resource.Sequence,
                action,
                resource.IsError,
                Marker: resource.Marker,
                ResourceUrn: resource.ResourceUrn,
                ParentUrn: resource.ParentUrn,
                ResourceType: resource.ResourceType,
                ResourceName: resource.ResourceName,
                Operation: resource.Operation,
                Status: resource.Status,
                Info: resource.Info,
                RawPayloadJson: resource.RawPayloadJson),
            StackDiagnosticTranscriptRow diagnostic => new UpdateViewChangeEntry(
                diagnostic.Key,
                kind,
                diagnostic.Sequence,
                action,
                diagnostic.IsError,
                Severity: diagnostic.Severity,
                ResourceUrn: diagnostic.ResourceUrn,
                Text: diagnostic.Message,
                RawPayloadJson: diagnostic.RawPayloadJson),
            StackTextTranscriptRow text => new UpdateViewChangeEntry(
                text.Key,
                kind,
                text.Sequence,
                action,
                text.IsError,
                Channel: text.Channel,
                Text: text.Text,
                RawPayloadJson: text.RawPayloadJson),
            StackSummaryTranscriptRow summary => new UpdateViewChangeEntry(
                summary.Key,
                kind,
                summary.Sequence,
                action,
                summary.IsError,
                Summary: summary.Summary,
                RawPayloadJson: summary.RawPayloadJson),
            StackUnknownTranscriptRow unknown => new UpdateViewChangeEntry(
                unknown.Key,
                kind,
                unknown.Sequence,
                action,
                unknown.IsError,
                Text: unknown.Text,
                RawPayloadJson: unknown.RawPayloadJson),
            _ => new UpdateViewChangeEntry(
                row.Key,
                kind,
                row.Sequence,
                action,
                row.IsError,
                RawPayloadJson: row.RawPayloadJson)
        };
    }

    private static UpdateViewChangeEntry MapChangeEntry(StackTranscriptRowDelta delta)
    {
        var row = delta.Current ?? delta.Previous
                  ?? throw new InvalidOperationException("Transcript delta did not contain a row.");
        var entry = MapChangeEntry(row);
        return entry with { Action = delta.Action.ToString().ToLowerInvariant() };
    }

    private static UpdateViewDelta BuildDelta(
        long? afterSequence,
        IReadOnlyList<StackTranscriptRowDelta> deltas,
        IReadOnlyList<StackTranscriptRow> previousRows,
        IReadOnlyList<StackTranscriptRow> currentRows,
        bool isInProgress)
    {
        var isIncremental = afterSequence.HasValue;
        var configurationChanged = isIncremental && deltas.Any(static delta => delta.Kind is StackTranscriptRowKind.Configuration);
        var resourcesChanged = isIncremental && deltas.Any(static delta => delta.Kind is StackTranscriptRowKind.Resource);
        var summaryChanged = isIncremental && deltas.Any(static delta => delta.Kind is StackTranscriptRowKind.Summary);
        var outputsChanged = isIncremental && !OutputsEqual(previousRows, currentRows);
        var changesChanged = isIncremental && deltas.Count > 0;
        var infoChanged = isIncremental && !isInProgress;

        return new UpdateViewDelta(
            IsIncremental: isIncremental,
            InfoChanged: infoChanged,
            ChangesChanged: changesChanged,
            ConfigurationChanged: configurationChanged,
            OutputsChanged: outputsChanged,
            ResourcesChanged: resourcesChanged,
            SummaryChanged: summaryChanged);
    }

    private static bool OutputsEqual(
        IReadOnlyList<StackTranscriptRow> previousRows,
        IReadOnlyList<StackTranscriptRow> currentRows)
    {
        var previousStack = previousRows
            .OfType<StackResourceTranscriptRow>()
            .LastOrDefault(static row => string.Equals(row.ResourceType, "pulumi:pulumi:Stack", StringComparison.Ordinal));
        var currentStack = currentRows
            .OfType<StackResourceTranscriptRow>()
            .LastOrDefault(static row => string.Equals(row.ResourceType, "pulumi:pulumi:Stack", StringComparison.Ordinal));

        return JsonElementDictionaryEquals(previousStack?.Metadata.New?.Outputs, currentStack?.Metadata.New?.Outputs);
    }

    private static List<StackTranscriptRow> BuildCheckpointTranscript(
        IReadOnlyList<MutableResource> resources,
        IReadOnlyDictionary<string, string?>? stackOutputs)
    {
        var rows = new List<StackTranscriptRow>(resources.Count);
        var sequence = 1L;

        foreach (var resource in resources)
        {
            var newState = new StepEventStateMetadata(
                Type: resource.Type,
                Urn: resource.Urn,
                Custom: !string.Equals(resource.Type, "pulumi:pulumi:Stack", StringComparison.Ordinal),
                Delete: null,
                Id: null,
                Parent: resource.ParentUrn,
                Protect: null,
                Taint: null,
                RetainOnDelete: null,
                Inputs: null,
                Outputs: string.Equals(resource.Type, "pulumi:pulumi:Stack", StringComparison.Ordinal)
                    ? ToJsonElementDictionary(stackOutputs)
                    : null,
                Provider: null,
                InitErrors: null,
                HideDiffs: null);
            var metadata = new StepEventMetadata(
                Op: "checkpoint",
                Urn: resource.Urn,
                Type: resource.Type,
                Old: null,
                New: newState,
                Keys: null,
                Diffs: null,
                DetailedDiff: null,
                Logical: null,
                Provider: null);

            rows.Add(new StackResourceTranscriptRow(
                Key: $"checkpoint-{sequence:D3}",
                Sequence: sequence,
                Marker: string.Empty,
                Operation: "checkpoint",
                ResourceUrn: resource.Urn,
                ParentUrn: resource.ParentUrn,
                ResourceType: resource.Type,
                ResourceName: resource.Name,
                Status: "checkpoint",
                Info: resource.Info,
                Metadata: metadata,
                RawPayloadJson: "{}",
                IsError: false));

            sequence++;
        }

        return rows;
    }

    private static bool JsonElementDictionaryEquals(
        IReadOnlyDictionary<string, JsonElement>? left,
        IReadOnlyDictionary<string, JsonElement>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var other) ||
                !string.Equals(entry.Value.GetRawText(), other.GetRawText(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static UpdateViewSummary? BuildSummaryFallback(IReadOnlyList<MutableResource> resources)
    {
        if (resources.Count == 0)
        {
            return null;
        }

        var resourceChanges = resources
            .GroupBy(static resource => resource.Operation, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

        return new UpdateViewSummary(
            IsPreview: false,
            MaybeCorrupt: false,
            DurationSeconds: null,
            ResourceChanges: resourceChanges.Count == 0 ? null : resourceChanges);
    }

    private sealed record MutableResource(
        string? Urn,
        string? ParentUrn,
        string? Type,
        string? Name,
        string Operation,
        string Status,
        string? Info,
        bool IsError)
    {
        public UpdateViewResourceEntry ToContract()
            => new(Urn, ParentUrn, Type, Name, Operation, Status, Info, IsError);
    }

    private sealed record DeploymentViewSnapshot(
        List<MutableResource> Resources,
        Dictionary<string, string?>? StackOutputs);

    private static Dictionary<string, JsonElement>? ToJsonElementDictionary(IReadOnlyDictionary<string, string?>? values)
    {
        if (values is not { Count: > 0 })
        {
            return null;
        }

        var dictionary = new Dictionary<string, JsonElement>(values.Count, StringComparer.Ordinal);
        foreach (var entry in values)
        {
            dictionary[entry.Key] = JsonSerializer.SerializeToElement(entry.Value, UpdateViewMaterializerJsonContext.Default.String);
        }

        return dictionary;
    }
}

internal sealed record RecoveredUpdateState(
    UpdateViewInfo Info,
    bool IsInProgress,
    string? ContinuationToken,
    long? LatestSequence,
    UpdateViewConfigEntry[] Configuration,
    UpdateViewOutputEntry[] Outputs,
    UpdateViewResourceEntry[] Resources,
    UpdateViewSummary? Summary,
    StackTranscriptRow[] TranscriptRows);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(string))]
internal partial class UpdateViewMaterializerJsonContext : JsonSerializerContext;
