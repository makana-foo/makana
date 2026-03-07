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
                deltas.Add(new StackConsoleLineDelta(line.Key, StackConsoleLineDeltaAction.Append, line.Sequence, line.Text));
                continue;
            }

            if (!string.Equals(previous.Text, line.Text, StringComparison.Ordinal))
            {
                deltas.Add(new StackConsoleLineDelta(line.Key, StackConsoleLineDeltaAction.Upsert, line.Sequence, line.Text));
            }
        }

        foreach (var line in previousLines)
        {
            if (currentByKey.ContainsKey(line.Key))
            {
                continue;
            }

            deltas.Add(new StackConsoleLineDelta(line.Key, StackConsoleLineDeltaAction.Delete, line.Sequence, string.Empty));
        }

        return deltas
            .OrderBy(static delta => delta.Sequence)
            .ThenBy(static delta => delta.Key, StringComparer.Ordinal)
            .ToArray();
    }
}
