using System.Text.Json;
using System.Text.RegularExpressions;
using Makana.StackUpdates.Models;
using Makana.StackUpdates.Pulumi;

namespace Makana.StackUpdates.Services;

public sealed partial class UpdateProjectionReducer
{
    public ProjectionState ReplayState(IReadOnlyList<EngineEvent> events)
    {
        var state = new ProjectionState();
        ApplyRange(state, events);
        return state;
    }

    public ProjectionState ReplayStateUntil(IReadOnlyList<EngineEvent> events, long maxSequenceInclusive)
    {
        var state = new ProjectionState();
        foreach (var ev in events.OrderBy(static item => item.Sequence))
        {
            if (ev.Sequence > maxSequenceInclusive)
            {
                break;
            }

            Apply(state, ev);
        }

        return state;
    }

    public UpdateProjection Replay(IReadOnlyList<EngineEvent> events)
        => ReplayState(events).BuildProjection();

    public UpdateProjection ReplayUntil(IReadOnlyList<EngineEvent> events, long maxSequenceInclusive)
    {
        return ReplayStateUntil(events, maxSequenceInclusive).BuildProjection();
    }

    public void ApplyRange(ProjectionState state, IReadOnlyList<EngineEvent> events)
    {
        foreach (var ev in events.OrderBy(static item => item.Sequence))
        {
            Apply(state, ev);
        }
    }

    public void Apply(ProjectionState state, EngineEvent ev)
    {
        ArgumentNullException.ThrowIfNull(state);

        state.LastSequence = Math.Max(state.LastSequence, ev.Sequence);

        if (TryApplyConfiguration(state, ev))
        {
            return;
        }

        if (TryApplyResourcePre(state, ev))
        {
            return;
        }

        if (TryApplyResourceOutputs(state, ev))
        {
            return;
        }

        if (TryApplyResourceFailure(state, ev))
        {
            return;
        }

        if (TryApplyDiagnostic(state, ev))
        {
            return;
        }

        if (TryApplyStdout(state, ev))
        {
            return;
        }

        if (TryApplySummary(state, ev))
        {
            return;
        }

        ApplyFallback(state, ev);
    }

    private static bool TryApplyConfiguration(ProjectionState state, EngineEvent ev)
    {
        if (ev.PreludeEvent?.Config is not { } config)
        {
            return false;
        }

        var entries = config
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .Select(static entry => new KeyValuePair<string, string>(entry.Key, entry.Value))
            .ToArray();

        state.IsPreview = true;
        state.Config = new UpdateConfigProjection(ev.Sequence, entries, FormatRawPayload(ev));
        state.Activity.Add(new UpdateActivityProjection(
            $"activity:config:{ev.Sequence}",
            ev.Sequence,
            UpdateActivityKind.Unknown,
            "Configuration loaded",
            null,
            null,
            false));
        return true;
    }

    private static bool TryApplyResourcePre(ProjectionState state, EngineEvent ev)
    {
        if (ev.ResourcePreEvent?.Metadata is not { } metadata)
        {
            return false;
        }

        var operation = metadata.Op ?? "update";
        UpsertResource(
            state,
            ev.Sequence,
            metadata,
            operation,
            PresentTenseForOperation(operation),
            string.Empty,
            IsFailureOperation(operation),
            FormatRawPayload(ev));
        UpdateStackOutputs(state, ev.Sequence, metadata);

        state.Activity.Add(new UpdateActivityProjection(
            $"activity:resource:{metadata.Urn}:{ev.Sequence}",
            ev.Sequence,
            UpdateActivityKind.Resource,
            $"{operation} {TryExtractResourceName(metadata.Urn) ?? "resource"}",
            null,
            metadata.Urn,
            false));
        return true;
    }

    private static bool TryApplyResourceOutputs(ProjectionState state, EngineEvent ev)
    {
        if (ev.ResOutputsEvent?.Metadata is not { } metadata)
        {
            return false;
        }

        var operation = metadata.Op ?? "same";
        UpsertResource(
            state,
            ev.Sequence,
            metadata,
            operation,
            PastTenseForOperation(operation),
            FormatResourceInfo(metadata),
            IsFailureOperation(operation),
            FormatRawPayload(ev));
        UpdateStackOutputs(state, ev.Sequence, metadata);

        state.Activity.Add(new UpdateActivityProjection(
            $"activity:resource:{metadata.Urn}:{ev.Sequence}",
            ev.Sequence,
            UpdateActivityKind.Resource,
            $"{PastTenseForOperation(operation)} {TryExtractResourceName(metadata.Urn) ?? "resource"}",
            FormatResourceInfo(metadata),
            metadata.Urn,
            IsFailureOperation(operation)));
        return true;
    }

    private static bool TryApplyResourceFailure(ProjectionState state, EngineEvent ev)
    {
        if (ev.ResOpFailedEvent?.Metadata is not { } metadata)
        {
            return false;
        }

        var operation = metadata.Op ?? "update";
        UpsertResource(
            state,
            ev.Sequence,
            metadata,
            operation,
            $"{PresentTenseForOperation(operation)} failed",
            FormatResourceInfo(metadata),
            true,
            FormatRawPayload(ev));
        UpdateStackOutputs(state, ev.Sequence, metadata);

        state.Activity.Add(new UpdateActivityProjection(
            $"activity:resource:{metadata.Urn}:{ev.Sequence}",
            ev.Sequence,
            UpdateActivityKind.Resource,
            $"{operation} failed {TryExtractResourceName(metadata.Urn) ?? "resource"}",
            FormatResourceInfo(metadata),
            metadata.Urn,
            true));
        return true;
    }

    private static bool TryApplyDiagnostic(ProjectionState state, EngineEvent ev)
    {
        if (ev.DiagnosticEvent is not { } diagnosticEvent)
        {
            return false;
        }

        var severity = NormalizeSeverity(diagnosticEvent.Severity);
        var message = NormalizeConsoleMarkup(diagnosticEvent.Message);
        var isError = string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(severity, "warning", StringComparison.OrdinalIgnoreCase) ||
                      severity.Contains("err", StringComparison.OrdinalIgnoreCase);

        state.Diagnostics.Add(new UpdateDiagnosticProjection(
            $"diagnostic:{ev.Sequence}",
            ev.Sequence,
            severity,
            diagnosticEvent.Urn,
            message,
            diagnosticEvent,
            FormatRawPayload(ev),
            isError));

        state.Activity.Add(new UpdateActivityProjection(
            $"activity:diagnostic:{ev.Sequence}",
            ev.Sequence,
            UpdateActivityKind.Diagnostic,
            severity,
            message,
            diagnosticEvent.Urn,
            isError));
        return true;
    }

    private static bool TryApplyStdout(ProjectionState state, EngineEvent ev)
    {
        if (ev.StdoutEvent is not { } stdoutEvent)
        {
            return false;
        }

        var text = NormalizeConsoleMarkup(stdoutEvent.Message);
        state.Stdout.Add(new UpdateStdoutProjection(
            $"stdout:{ev.Sequence}",
            ev.Sequence,
            "info",
            text,
            stdoutEvent,
            FormatRawPayload(ev),
            false));

        state.Activity.Add(new UpdateActivityProjection(
            $"activity:stdout:{ev.Sequence}",
            ev.Sequence,
            UpdateActivityKind.Stdout,
            "stdout",
            text,
            null,
            false));
        return true;
    }

    private static bool TryApplySummary(ProjectionState state, EngineEvent ev)
    {
        if (ev.SummaryEvent is not { } summaryEvent)
        {
            return false;
        }

        state.IsPreview = summaryEvent.IsPreview;
        state.Summary = new UpdateSummaryProjection(
            $"summary:{ev.Sequence}",
            ev.Sequence,
            BuildSummary(summaryEvent),
            summaryEvent,
            FormatRawPayload(ev));

        state.Activity.Add(new UpdateActivityProjection(
            $"activity:summary:{ev.Sequence}",
            ev.Sequence,
            UpdateActivityKind.Summary,
            "summary",
            BuildSummary(summaryEvent),
            null,
            false));
        return true;
    }

    private static void ApplyFallback(ProjectionState state, EngineEvent ev)
    {
        state.Activity.Add(new UpdateActivityProjection(
            $"activity:unknown:{ev.Sequence}",
            ev.Sequence,
            UpdateActivityKind.Unknown,
            DescribeUnhandledEvent(ev),
            null,
            null,
            false));
    }

    private static void UpsertResource(
        ProjectionState state,
        long sequence,
        StepEventMetadata metadata,
        string operation,
        string status,
        string info,
        bool isError,
        string rawPayloadJson)
    {
        var identity = ResourceIdentity(metadata.Urn);
        var key = $"resource:{identity}";
        var marker = MarkerForOperation(operation);
        var resourceType = metadata.Type ?? TryExtractResourceType(metadata.Urn);
        var parentUrn = metadata.New?.Parent ?? metadata.Old?.Parent;
        var resourceName = TryExtractResourceName(metadata.Urn);

        if (state.ResourceIndex.TryGetValue(identity, out var existingIndex))
        {
            var existing = state.Resources[existingIndex];
            state.Resources[existingIndex] = existing with
            {
                Sequence = sequence,
                Marker = marker,
                Operation = operation,
                ResourceUrn = metadata.Urn,
                ParentUrn = parentUrn,
                ResourceType = resourceType,
                ResourceName = resourceName,
                Status = status,
                Info = info,
                Metadata = metadata,
                RawPayloadJson = rawPayloadJson,
                IsError = existing.IsError || isError
            };
            return;
        }

        state.ResourceIndex[identity] = state.Resources.Count;
        state.Resources.Add(new UpdateResourceProjection(
            key,
            sequence,
            sequence,
            marker,
            operation,
            metadata.Urn,
            parentUrn,
            resourceType,
            resourceName,
            status,
            info,
            metadata,
            rawPayloadJson,
            isError));
    }

    private static void UpdateStackOutputs(ProjectionState state, long sequence, StepEventMetadata metadata)
    {
        var resourceType = metadata.Type ?? TryExtractResourceType(metadata.Urn);
        if (!string.Equals(resourceType, "pulumi:pulumi:Stack", StringComparison.Ordinal))
        {
            return;
        }

        state.StackOutputs = new UpdateStackOutputsProjection(
            sequence,
            ReadOutputs(metadata.Old?.Outputs),
            ReadOutputs(metadata.New?.Outputs));
    }

    private static string BuildSummary(SummaryEvent summaryEvent)
    {
        if (summaryEvent.ResourceChanges.Count == 0)
        {
            return "No changes.";
        }

        return string.Join(", ",
            summaryEvent.ResourceChanges
                .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
                .Select(static entry => $"{entry.Value} {entry.Key}"));
    }

    private static string DescribeUnhandledEvent(EngineEvent ev)
    {
        return ev switch
        {
            { CancelEvent: not null } => "cancel",
            { PolicyEvent: not null } => "policy",
            { PolicyRemediationEvent: not null } => "policy remediation",
            { PolicyLoadEvent: not null } => "policy load",
            { PolicyAnalyzeSummaryEvent: not null } => "policy analyze summary",
            { PolicyRemediateSummaryEvent: not null } => "policy remediate summary",
            { PolicyAnalyzeStackSummaryEvent: not null } => "policy analyze stack summary",
            { StartDebuggingEvent: not null } => "start debugging",
            { ProgressEvent: not null } => "progress",
            { ErrorEvent: not null } => "error",
            _ => "unknown"
        };
    }

    private static string ResourceIdentity(string? urn)
        => string.IsNullOrWhiteSpace(urn) ? "<unknown>" : urn;

    private static string NormalizeSeverity(string? severity)
        => string.IsNullOrWhiteSpace(severity) ? "info" : severity.Trim();

    private static string MarkerForOperation(string operation)
        => operation.ToLowerInvariant() switch
        {
            "create" => "+",
            "import" => "<",
            "delete" => "-",
            "replace" => "+-",
            "same" => " ",
            _ => "~"
        };

    private static string PresentTenseForOperation(string operation)
        => operation.ToLowerInvariant() switch
        {
            "create" => "creating",
            "update" => "updating",
            "replace" => "replacing",
            "delete" => "deleting",
            "same" => "same",
            "refresh" => "refreshing",
            "read" => "reading",
            "import" => "importing",
            "discard" => "discarding",
            _ => operation
        };

    private static string PastTenseForOperation(string operation)
        => operation.ToLowerInvariant() switch
        {
            "create" => "created",
            "update" => "updated",
            "replace" => "replaced",
            "delete" => "deleted",
            "same" => "same",
            "refresh" => "refreshed",
            "read" => "read",
            "import" => "imported",
            "discard" => "discarded",
            _ => operation
        };

    private static bool IsFailureOperation(string operation)
        => operation.Contains("fail", StringComparison.OrdinalIgnoreCase);

    private static string? TryExtractResourceType(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return null;
        }

        var parts = urn.Split("::", StringSplitOptions.None);
        return parts.Length >= 3 ? parts[^2] : null;
    }

    private static string? TryExtractResourceName(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return null;
        }

        var parts = urn.Split("::", StringSplitOptions.None);
        return parts.Length >= 1 ? parts[^1] : null;
    }

    private static string FormatResourceInfo(StepEventMetadata metadata)
    {
        if (metadata.New?.InitErrors is { Length: > 0 } initErrors)
        {
            return initErrors[0];
        }

        var message = TryExtractMessage(metadata.New?.Outputs) ??
                      TryExtractMessage(metadata.New?.Inputs) ??
                      TryExtractMessage(metadata.Old?.Outputs) ??
                      TryExtractMessage(metadata.Old?.Inputs);

        return message ?? string.Empty;
    }

    private static string? TryExtractMessage(Dictionary<string, JsonElement>? values)
    {
        if (values is null)
        {
            return null;
        }

        foreach (var key in new[] { "message", "result", "name" })
        {
            if (values.TryGetValue(key, out var value))
            {
                return NormalizeConsoleMarkup(DescribeJsonValue(value));
            }
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string?>? ReadOutputs(Dictionary<string, JsonElement>? outputs)
    {
        if (outputs is not { Count: > 0 })
        {
            return null;
        }

        var result = new Dictionary<string, string?>(outputs.Count, StringComparer.Ordinal);
        foreach (var property in outputs)
        {
            result[property.Key] = DescribeJsonValue(property.Value);
        }

        return result;
    }

    private static string DescribeJsonValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "null",
            _ => value.GetRawText()
        };

    private static string NormalizeConsoleMarkup(string value)
    {
        var cleaned = ControlSequenceRegex().Replace(value, string.Empty);
        cleaned = cleaned.Replace("\r\n", "\n", StringComparison.Ordinal);
        cleaned = cleaned.Replace("\r", "\n", StringComparison.Ordinal);
        return cleaned.TrimEnd('\n');
    }

    private static string FormatRawPayload(EngineEvent value)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        JsonSerializer.Serialize(writer, value, PulumiEngineEventJsonContext.Default.EngineEvent);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    [GeneratedRegex(@"<\{%\w+%\}>|\x1B\[[0-9;]*[A-Za-z]")]
    private static partial Regex ControlSequenceRegex();

    public sealed class ProjectionState
    {
        internal long LastSequence { get; set; } = -1;
        internal bool? IsPreview { get; set; }
        internal UpdateConfigProjection? Config { get; set; }
        internal UpdateStackOutputsProjection? StackOutputs { get; set; }
        internal List<UpdateResourceProjection> Resources { get; } = [];
        internal Dictionary<string, int> ResourceIndex { get; } = new(StringComparer.Ordinal);
        internal List<UpdateDiagnosticProjection> Diagnostics { get; } = [];
        internal List<UpdateStdoutProjection> Stdout { get; } = [];
        internal UpdateSummaryProjection? Summary { get; set; }
        internal List<UpdateActivityProjection> Activity { get; } = [];

        public long Sequence => LastSequence;

        public ProjectionState Clone()
        {
            var clone = new ProjectionState
            {
                LastSequence = LastSequence,
                IsPreview = IsPreview,
                Config = Config is null
                    ? null
                    : new UpdateConfigProjection(Config.Sequence, [.. Config.Entries], Config.RawPayloadJson),
                StackOutputs = StackOutputs is null
                    ? null
                    : new UpdateStackOutputsProjection(
                        StackOutputs.Sequence,
                        StackOutputs.OldOutputs is null ? null : new Dictionary<string, string?>(StackOutputs.OldOutputs, StringComparer.Ordinal),
                        StackOutputs.NewOutputs is null ? null : new Dictionary<string, string?>(StackOutputs.NewOutputs, StringComparer.Ordinal)),
                Summary = Summary
            };

            clone.Resources.AddRange(Resources);
            foreach (var entry in ResourceIndex)
            {
                clone.ResourceIndex[entry.Key] = entry.Value;
            }

            clone.Diagnostics.AddRange(Diagnostics);
            clone.Stdout.AddRange(Stdout);
            clone.Activity.AddRange(Activity);
            return clone;
        }

        public UpdateProjection BuildProjection()
            => new(
                LastSequence,
                IsPreview,
                Config,
                StackOutputs,
                Resources.OrderBy(static item => item.FirstSequence).ToArray(),
                Diagnostics.OrderBy(static item => item.Sequence).ToArray(),
                Stdout.OrderBy(static item => item.Sequence).ToArray(),
                Summary,
                Activity.OrderBy(static item => item.Sequence).ToArray());
    }
}
