namespace Makana.StackUpdates.Models;

public enum StackConsoleLineDeltaAction
{
    Append,
    Upsert,
    Delete
}

public enum StackSummaryLineKind
{
    Header,
    Resource,
    SectionHeading,
    Plain
}

public sealed record StackSummaryLineSemantic(
    StackSummaryLineKind Kind,
    string? Tree,
    string? TypeText,
    string? Name,
    string? Status,
    string? Info,
    string? HeadingText);

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
    StackSummaryLineSemantic? SummarySemantic = null);
