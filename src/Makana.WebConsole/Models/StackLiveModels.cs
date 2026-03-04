using Makana.WebConsole.Services;

namespace Makana.WebConsole.Models;

public enum StackTranscriptRowKind
{
    Configuration,
    Resource,
    Diagnostic,
    Text,
    Summary,
    Unknown
}

public abstract record StackTranscriptRow(
    string Key,
    StackTranscriptRowKind Kind,
    string RawPayloadJson,
    bool IsError);

public sealed record StackConfigurationTranscriptRow(
    string Key,
    IReadOnlyList<KeyValuePair<string, string>> Entries,
    string RawPayloadJson)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Configuration, RawPayloadJson, false);

public sealed record StackResourceTranscriptRow(
    string Key,
    string Marker,
    string Operation,
    string? ResourceUrn,
    string? ParentUrn,
    string? ResourceType,
    string? ResourceName,
    string? Status,
    string? Info,
    string RawPayloadJson,
    bool IsError)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Resource, RawPayloadJson, IsError);

public sealed record StackDiagnosticTranscriptRow(
    string Key,
    string Severity,
    string? ResourceUrn,
    string Message,
    string RawPayloadJson,
    bool IsError)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Diagnostic, RawPayloadJson, IsError);

public sealed record StackTextTranscriptRow(
    string Key,
    string Channel,
    string Text,
    string RawPayloadJson,
    bool IsError)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Text, RawPayloadJson, IsError);

public sealed record StackSummaryTranscriptRow(
    string Key,
    string Summary,
    string RawPayloadJson)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Summary, RawPayloadJson, false);

public sealed record StackUnknownTranscriptRow(
    string Key,
    string Text,
    string RawPayloadJson)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Unknown, RawPayloadJson, false);

public sealed record StackLivePanelViewModel(
    string Org,
    string Project,
    string Stack,
    string? UpdateId,
    bool IsInProgress,
    string StatusText,
    UpdateInfo? LatestInfo,
    IReadOnlyList<StackTranscriptRow> TranscriptRows,
    IReadOnlyList<UpdateInfo> RecentUpdates)
{
    public bool IsPreview =>
        string.Equals(LatestInfo?.Kind, "preview", StringComparison.OrdinalIgnoreCase);

    public string SummaryStateLabel => IsPreview ? "Plan" : "Status";

    public IReadOnlyList<StackResourceTranscriptRow> ResourceRows =>
        TranscriptRows.OfType<StackResourceTranscriptRow>().ToArray();

    public string SummaryTranscript => StackUpdateTextFormatter.BuildSummaryTranscript(ResourceRows, TranscriptRows);

    public string DiagnosticTranscript => StackUpdateTextFormatter.BuildDiagnosticTranscript(TranscriptRows, Org, Project, Stack);

    public IReadOnlyList<string> DiffLines => StackUpdateTextFormatter.BuildDiffLines(TranscriptRows);

    public StackConsoleDocument SummaryConsole => StackUpdateConsoleRenderer.Render(StackConsoleViewKind.Summary, SummaryTranscript);

    public StackConsoleDocument DiffConsole => StackUpdateConsoleRenderer.Render(StackConsoleViewKind.Diff, string.Join('\n', DiffLines));

    public StackConsoleDocument DiagnosticConsole => StackUpdateConsoleRenderer.Render(StackConsoleViewKind.Diagnostic, DiagnosticTranscript);

    public static StackLivePanelViewModel Empty(string org, string project, string stack)
    {
        return new StackLivePanelViewModel(
            org,
            project,
            stack,
            null,
            false,
            "No active update",
            null,
            [],
            []);
    }
}

public enum StackConsoleViewKind
{
    Summary,
    Diff,
    Diagnostic
}

public sealed record StackConsoleDocument(
    StackConsoleViewKind ViewKind,
    IReadOnlyList<StackConsoleLine> Lines);

public sealed record StackConsoleLine(
    string Text,
    string CssClass,
    IReadOnlyList<StackConsoleSegment> Segments);

public sealed record StackConsoleSegment(
    string Text,
    string CssClass);
