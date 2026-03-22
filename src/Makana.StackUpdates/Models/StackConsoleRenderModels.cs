namespace Makana.StackUpdates.Models;

public enum StackConsoleViewKind
{
    Summary,
    Diff,
    Diagnostic
}

public sealed record StackConsoleRenderedDocument(
    StackConsoleViewKind ViewKind,
    IReadOnlyList<StackConsoleRenderedLine> Lines);

public sealed record StackConsoleRenderedLine(
    string Key,
    string Text,
    string CssClass,
    IReadOnlyList<StackConsoleRenderedSegment> Segments);

public sealed record StackConsoleRenderedSegment(
    string Text,
    string CssClass);

public sealed record StackConsoleRenderedDelta(
    string Key,
    string Action,
    StackConsoleRenderedLine? Line);
