namespace Makana.StackUpdates.Models;

public enum StackConsoleLineDeltaAction
{
    Append,
    Upsert,
    Delete
}

public sealed record StackConsoleLineProjection(
    string Key,
    long Sequence,
    string Text);

public sealed record StackConsoleLineDelta(
    string Key,
    StackConsoleLineDeltaAction Action,
    long Sequence,
    string Text);
