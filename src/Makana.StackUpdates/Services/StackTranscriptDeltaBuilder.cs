using Makana.StackUpdates.Models;

namespace Makana.StackUpdates.Services;

public sealed class StackTranscriptDeltaBuilder
{
    public IReadOnlyList<StackTranscriptRowDelta> Build(
        IReadOnlyList<StackTranscriptRow> previousRows,
        IReadOnlyList<StackTranscriptRow> currentRows)
    {
        var deltas = new List<StackTranscriptRowDelta>();

        var previousByKey = previousRows.ToDictionary(static row => row.Key, StringComparer.Ordinal);
        var currentByKey = currentRows.ToDictionary(static row => row.Key, StringComparer.Ordinal);

        foreach (var row in currentRows)
        {
            if (!previousByKey.TryGetValue(row.Key, out var previous))
            {
                deltas.Add(new StackTranscriptRowDelta(
                    row.Key,
                    row.Kind,
                    StackTranscriptRowDeltaAction.Append,
                    row.Sequence,
                    row,
                    null));
                continue;
            }

            if (!Equivalent(previous, row))
            {
                deltas.Add(new StackTranscriptRowDelta(
                    row.Key,
                    row.Kind,
                    StackTranscriptRowDeltaAction.Upsert,
                    row.Sequence,
                    row,
                    previous));
            }
        }

        foreach (var row in previousRows)
        {
            if (currentByKey.ContainsKey(row.Key))
            {
                continue;
            }

            deltas.Add(new StackTranscriptRowDelta(
                row.Key,
                row.Kind,
                StackTranscriptRowDeltaAction.Delete,
                row.Sequence,
                null,
                row));
        }

        return deltas
            .OrderBy(static delta => delta.Sequence)
            .ThenBy(static delta => delta.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool Equivalent(StackTranscriptRow left, StackTranscriptRow right)
        => (left, right) switch
        {
            (StackConfigurationTranscriptRow a, StackConfigurationTranscriptRow b) => Equivalent(a, b),
            (StackResourceTranscriptRow a, StackResourceTranscriptRow b) => Equivalent(a, b),
            (StackDiagnosticTranscriptRow a, StackDiagnosticTranscriptRow b) => Equivalent(a, b),
            (StackTextTranscriptRow a, StackTextTranscriptRow b) => Equivalent(a, b),
            (StackSummaryTranscriptRow a, StackSummaryTranscriptRow b) => Equivalent(a, b),
            (StackUnknownTranscriptRow a, StackUnknownTranscriptRow b) => Equivalent(a, b),
            _ => false
        };

    private static bool Equivalent(StackConfigurationTranscriptRow left, StackConfigurationTranscriptRow right)
        => left.Key == right.Key
           && left.Sequence == right.Sequence
           && left.RawPayloadJson == right.RawPayloadJson
           && left.Entries.SequenceEqual(right.Entries);

    private static bool Equivalent(StackResourceTranscriptRow left, StackResourceTranscriptRow right)
        => left.Key == right.Key
           && left.Sequence == right.Sequence
           && left.Marker == right.Marker
           && left.Operation == right.Operation
           && left.ResourceUrn == right.ResourceUrn
           && left.ParentUrn == right.ParentUrn
           && left.ResourceType == right.ResourceType
           && left.ResourceName == right.ResourceName
           && left.Status == right.Status
           && left.Info == right.Info
           && left.RawPayloadJson == right.RawPayloadJson
           && left.IsError == right.IsError;

    private static bool Equivalent(StackDiagnosticTranscriptRow left, StackDiagnosticTranscriptRow right)
        => left.Key == right.Key
           && left.Sequence == right.Sequence
           && left.Severity == right.Severity
           && left.ResourceUrn == right.ResourceUrn
           && left.Message == right.Message
           && left.RawPayloadJson == right.RawPayloadJson
           && left.IsError == right.IsError;

    private static bool Equivalent(StackTextTranscriptRow left, StackTextTranscriptRow right)
        => left.Key == right.Key
           && left.Sequence == right.Sequence
           && left.Channel == right.Channel
           && left.Text == right.Text
           && left.RawPayloadJson == right.RawPayloadJson
           && left.IsError == right.IsError;

    private static bool Equivalent(StackSummaryTranscriptRow left, StackSummaryTranscriptRow right)
        => left.Key == right.Key
           && left.Sequence == right.Sequence
           && left.Summary == right.Summary
           && left.RawPayloadJson == right.RawPayloadJson;

    private static bool Equivalent(StackUnknownTranscriptRow left, StackUnknownTranscriptRow right)
        => left.Key == right.Key
           && left.Sequence == right.Sequence
           && left.Text == right.Text
           && left.RawPayloadJson == right.RawPayloadJson;
}
