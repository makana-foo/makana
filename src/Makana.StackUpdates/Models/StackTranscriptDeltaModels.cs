namespace Makana.StackUpdates.Models;

public enum StackTranscriptRowDeltaAction
{
    Append,
    Upsert,
    Delete
}

public sealed record StackTranscriptRowDelta(
    string Key,
    StackTranscriptRowKind Kind,
    StackTranscriptRowDeltaAction Action,
    long Sequence,
    StackTranscriptRow? Current,
    StackTranscriptRow? Previous);
