namespace Makana.StackUpdates.Models;

public enum StackConsoleLineDeltaAction
{
    Append,
    InsertBefore,
    Upsert,
    Delete
}

public enum StackSummaryLineKind
{
    Header,
    Resource,
    SectionHeading,
    SectionDetail,
    DiagnosticDetail,
    Plain
}

public sealed record StackSummaryLineSemantic(
    StackSummaryLineKind Kind,
    string? Tree,
    string? TypeText,
    string? Name,
    string? Status,
    string? Info,
    string? HeadingText,
    bool ShowInfoColumn);

public sealed record StackConsoleLineProjection(
    string Key,
    long Sequence,
    string Text,
    StackSummaryLineSemantic? SummarySemantic = null);

public sealed record StackConsoleLineDelta(
    string Key,
    StackConsoleLineDeltaAction Action,
    long Sequence,
    string Text,
    StackSummaryLineSemantic? SummarySemantic = null,
    string? AnchorKey = null);
