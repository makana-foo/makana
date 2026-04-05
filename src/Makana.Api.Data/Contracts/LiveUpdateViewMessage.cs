using System.Text.Json.Serialization;

namespace Makana.Api.Contracts;

public record LiveUpdateViewChanges(
    [property: JsonPropertyName("hasEvents")] bool? HasEvents = null,
    [property: JsonPropertyName("messageCount")] int? MessageCount = null,
    [property: JsonPropertyName("entries")] UpdateViewChangeEntry[]? Entries = null,
    [property: JsonPropertyName("deltaEntries")] UpdateViewChangeEntry[]? DeltaEntries = null,
    [property: JsonPropertyName("summaryLines")] UpdateViewConsoleLine[]? SummaryLines = null,
    [property: JsonPropertyName("diffLines")] UpdateViewConsoleLine[]? DiffLines = null,
    [property: JsonPropertyName("diagnosticLines")] UpdateViewConsoleLine[]? DiagnosticLines = null,
    [property: JsonPropertyName("summaryLineDeltas")] UpdateViewConsoleLineDelta[]? SummaryLineDeltas = null,
    [property: JsonPropertyName("diffLineDeltas")] UpdateViewConsoleLineDelta[]? DiffLineDeltas = null,
    [property: JsonPropertyName("diagnosticLineDeltas")] UpdateViewConsoleLineDelta[]? DiagnosticLineDeltas = null
);

public record LiveUpdateViewMessage(
    [property: JsonPropertyName("stack")] StackPageMetadata? Stack = null,
    [property: JsonPropertyName("info")] UpdateViewInfo? Info = null,
    [property: JsonPropertyName("header")] UpdateViewHeader? Header = null,
    [property: JsonPropertyName("history")] UpdateViewHistoryEntry[]? History = null,
    [property: JsonPropertyName("configuration")] UpdateViewConfigEntry[]? Configuration = null,
    [property: JsonPropertyName("outputs")] UpdateViewOutputEntry[]? Outputs = null,
    [property: JsonPropertyName("resources")] UpdateViewResourceEntry[]? Resources = null,
    [property: JsonPropertyName("summary")] UpdateViewSummary? Summary = null,
    [property: JsonPropertyName("changes")] LiveUpdateViewChanges? Changes = null,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken = null,
    [property: JsonPropertyName("delta")] UpdateViewDelta? Delta = null)
{
    public static LiveUpdateViewMessage FromResponse(UpdateViewResponse response)
    {
        var delta = response.Delta;
        if (delta is null || !delta.IsIncremental)
        {
            return new LiveUpdateViewMessage(
                Stack: response.Stack,
                Info: response.Info,
                Header: response.Header,
                History: response.History,
                Configuration: response.Configuration,
                Outputs: response.Outputs,
                Resources: response.Resources,
                Summary: response.Summary,
                Changes: new LiveUpdateViewChanges(
                    HasEvents: response.Changes.HasEvents,
                    MessageCount: response.Changes.MessageCount,
                    Entries: response.Changes.Entries,
                    DeltaEntries: response.Changes.DeltaEntries,
                    SummaryLines: response.Changes.SummaryLines,
                    DiffLines: response.Changes.DiffLines,
                    DiagnosticLines: response.Changes.DiagnosticLines,
                    SummaryLineDeltas: response.Changes.SummaryLineDeltas,
                    DiffLineDeltas: response.Changes.DiffLineDeltas,
                    DiagnosticLineDeltas: response.Changes.DiagnosticLineDeltas),
                ContinuationToken: response.ContinuationToken,
                Delta: delta);
        }

        return new LiveUpdateViewMessage(
            Info: delta.InfoChanged ? response.Info : null,
            Header: delta.InfoChanged ? response.Header : null,
            History: delta.InfoChanged ? response.History : null,
            Configuration: delta.ConfigurationChanged ? response.Configuration : null,
            Outputs: delta.OutputsChanged ? response.Outputs : null,
            Resources: delta.ResourcesChanged ? response.Resources : null,
            Summary: delta.InfoChanged || delta.SummaryChanged ? response.Summary : null,
            Changes: BuildChanges(response.Changes),
            ContinuationToken: response.ContinuationToken,
            Delta: delta);
    }

    private static LiveUpdateViewChanges? BuildChanges(UpdateViewChanges changes)
    {
        var hasPayload =
            changes.Entries is { Length: > 0 } ||
            changes.DeltaEntries is { Length: > 0 } ||
            changes.SummaryLines is { Length: > 0 } ||
            changes.DiffLines is { Length: > 0 } ||
            changes.DiagnosticLines is { Length: > 0 } ||
            changes.SummaryLineDeltas is { Length: > 0 } ||
            changes.DiffLineDeltas is { Length: > 0 } ||
            changes.DiagnosticLineDeltas is { Length: > 0 };

        if (!hasPayload)
        {
            return null;
        }

        return new LiveUpdateViewChanges(
            HasEvents: changes.HasEvents,
            MessageCount: changes.MessageCount,
            Entries: changes.Entries,
            DeltaEntries: changes.DeltaEntries,
            SummaryLines: changes.SummaryLines,
            DiffLines: changes.DiffLines,
            DiagnosticLines: changes.DiagnosticLines,
            SummaryLineDeltas: changes.SummaryLineDeltas,
            DiffLineDeltas: changes.DiffLineDeltas,
            DiagnosticLineDeltas: changes.DiagnosticLineDeltas);
    }
}
