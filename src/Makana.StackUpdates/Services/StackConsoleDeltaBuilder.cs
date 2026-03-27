using Makana.StackUpdates.Models;

namespace Makana.StackUpdates.Services;

public sealed class StackConsoleDeltaBuilder
{
    public IReadOnlyList<StackConsoleLineDelta> Build(
        IReadOnlyList<StackConsoleLineProjection> previousLines,
        IReadOnlyList<StackConsoleLineProjection> currentLines)
    {
        var deltas = new List<StackConsoleLineDelta>();
        var previousByKey = previousLines.ToDictionary(static line => line.Key, StringComparer.Ordinal);
        var currentByKey = currentLines.ToDictionary(static line => line.Key, StringComparer.Ordinal);

        foreach (var line in currentLines)
        {
            if (!previousByKey.TryGetValue(line.Key, out var previous))
            {
                deltas.Add(new StackConsoleLineDelta(
                    line.Key,
                    GetInsertAction(previousByKey, currentLines, line.Key, out var anchorKey),
                    line.Sequence,
                    line.Text,
                    line.SummarySemantic,
                    anchorKey));
                continue;
            }

            if (!string.Equals(previous.Text, line.Text, StringComparison.Ordinal) ||
                !Equals(previous.SummarySemantic, line.SummarySemantic))
            {
                deltas.Add(new StackConsoleLineDelta(line.Key, StackConsoleLineDeltaAction.Upsert, line.Sequence, line.Text, line.SummarySemantic));
            }
        }

        foreach (var line in previousLines)
        {
            if (currentByKey.ContainsKey(line.Key))
            {
                continue;
            }

            deltas.Add(new StackConsoleLineDelta(line.Key, StackConsoleLineDeltaAction.Delete, line.Sequence, string.Empty, line.SummarySemantic));
        }

        return deltas;
    }

    private static StackConsoleLineDeltaAction GetInsertAction(
        IReadOnlyDictionary<string, StackConsoleLineProjection> previousByKey,
        IReadOnlyList<StackConsoleLineProjection> currentLines,
        string key,
        out string? anchorKey)
    {
        anchorKey = null;
        for (var index = 0; index < currentLines.Count; index++)
        {
            if (!string.Equals(currentLines[index].Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            for (var nextIndex = index + 1; nextIndex < currentLines.Count; nextIndex++)
            {
                var candidateKey = currentLines[nextIndex].Key;
                if (previousByKey.ContainsKey(candidateKey))
                {
                    anchorKey = candidateKey;
                    return StackConsoleLineDeltaAction.InsertBefore;
                }
            }

            break;
        }

        return StackConsoleLineDeltaAction.Append;
    }
}
