using System.Text.RegularExpressions;
using Makana.StackUpdates.Models;

namespace Makana.StackUpdates.Services;

public static partial class StackConsoleSemanticRenderer
{
    private const string UnknownSignatureValue = "04da6b54-80e4-46f7-96ec-b56ff0331ba9";

    public static StackConsoleRenderedDocument Render(
        StackConsoleViewKind viewKind,
        IReadOnlyList<StackConsoleLineProjection>? lines,
        string text)
    {
        if (lines is { Count: > 0 })
        {
            return new StackConsoleRenderedDocument(
                viewKind,
                lines.Select(line => RenderLine(viewKind, line)).ToArray());
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var renderedLines = normalized.Split('\n', StringSplitOptions.None)
            .Select((line, index) => RenderLine(
                viewKind,
                new StackConsoleLineProjection($"{viewKind.ToString().ToLowerInvariant()}:{index}", index, line)))
            .ToArray();

        return new StackConsoleRenderedDocument(viewKind, renderedLines);
    }

    public static IReadOnlyList<StackConsoleRenderedDelta> RenderLineDeltas(
        StackConsoleViewKind viewKind,
        IReadOnlyList<StackConsoleLineDelta> deltas)
        => deltas.Select(delta => new StackConsoleRenderedDelta(
                delta.Key,
                delta.Action.ToString().ToLowerInvariant(),
                delta.Action == StackConsoleLineDeltaAction.Delete
                    ? null
                    : RenderLine(viewKind, new StackConsoleLineProjection(delta.Key, delta.Sequence, delta.Text, delta.SummarySemantic))))
            .ToArray();

    public static string Flatten(StackConsoleRenderedDocument document)
        => string.Join('\n', document.Lines.Select(line => string.Concat(line.Segments.Select(segment => segment.Text))));

    private static StackConsoleRenderedLine RenderLine(StackConsoleViewKind viewKind, StackConsoleLineProjection line)
        => viewKind switch
        {
            StackConsoleViewKind.Summary => RenderSummaryLine(line),
            StackConsoleViewKind.Diff => RenderDiffLine(line.Key, line.Text),
            StackConsoleViewKind.Diagnostic => RenderDiagnosticLine(line.Key, line.Text),
            _ => PlainLine(line.Key, line.Text, "stack-console-line")
        };

    private static StackConsoleRenderedLine RenderSummaryLine(StackConsoleLineProjection projection)
    {
        var key = projection.Key;
        var line = projection.Text;

        if (projection.SummarySemantic is { } semantic)
        {
            return semantic.Kind switch
            {
                StackSummaryLineKind.Header => RenderSummaryHeaderLine(key, line, semantic),
                StackSummaryLineKind.Resource => RenderSummaryResourceLine(key, line, semantic),
                StackSummaryLineKind.SectionHeading => HeadingWithRemainder(key, semantic.HeadingText ?? line),
                _ => RenderSummaryLineFromText(key, line)
            };
        }

        return RenderSummaryLineFromText(key, line);
    }

    private static StackConsoleRenderedLine RenderSummaryLineFromText(string key, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return PlainLine(key, line, "stack-console-line stack-console-line--blank");
        }

        if (line.StartsWith("     Type", StringComparison.Ordinal))
        {
            return new StackConsoleRenderedLine(
                key,
                line,
                "stack-console-line stack-console-line--summary-grid stack-console-line--header",
                [
                    new StackConsoleRenderedSegment(line[..5], "stack-console-segment stack-console-segment--whitespace"),
                    new StackConsoleRenderedSegment(line[5..28], "stack-console-segment stack-console-segment--column-heading"),
                    new StackConsoleRenderedSegment(line[28..58], "stack-console-segment stack-console-segment--column-heading"),
                    new StackConsoleRenderedSegment(line[58..69], "stack-console-segment stack-console-segment--column-heading"),
                    new StackConsoleRenderedSegment(line[69..], "stack-console-segment stack-console-segment--column-heading"),
                ]);
        }

        if (line is "Outputs:" or "Resources:" || line.StartsWith("Duration:", StringComparison.Ordinal))
        {
            return HeadingWithRemainder(key, line);
        }

        if (line.StartsWith("     ", StringComparison.Ordinal) && line.Length >= 69)
        {
            var typeColumn = line[5..28];
            var nameColumn = line[28..58];
            var statusColumn = line[58..69];
            var infoColumn = line.Length > 69 ? line[69..] : string.Empty;

            var typeSegments = RenderSummaryTypeColumn(typeColumn);
            var segments = new List<StackConsoleRenderedSegment>(typeSegments.Count + 4)
            {
                new(line[..5], "stack-console-segment stack-console-segment--whitespace")
            };
            segments.AddRange(typeSegments);
            segments.Add(new StackConsoleRenderedSegment(nameColumn, "stack-console-segment stack-console-segment--summary-name"));
            segments.Add(new StackConsoleRenderedSegment(statusColumn, "stack-console-segment stack-console-segment--summary-status"));
            segments.Add(new StackConsoleRenderedSegment(infoColumn, "stack-console-segment stack-console-segment--summary-info"));
            return new StackConsoleRenderedLine(key, line, "stack-console-line stack-console-line--summary-grid stack-console-line--summary-row", segments);
        }

        return RenderIndentedDiffLikeLine(key, line, "stack-console-line stack-console-line--summary-detail");
    }

    private static StackConsoleRenderedLine RenderSummaryHeaderLine(string key, string line, StackSummaryLineSemantic semantic)
    {
        return new StackConsoleRenderedLine(
            key,
            line,
            "stack-console-line stack-console-line--summary-grid stack-console-line--header",
            [
                new StackConsoleRenderedSegment("     ", "stack-console-segment stack-console-segment--whitespace"),
                new StackConsoleRenderedSegment(semantic.TypeText ?? "Type", "stack-console-segment stack-console-segment--column-heading"),
                new StackConsoleRenderedSegment(semantic.Name ?? "Name", "stack-console-segment stack-console-segment--column-heading"),
                new StackConsoleRenderedSegment(semantic.Status ?? "Status", "stack-console-segment stack-console-segment--column-heading"),
                new StackConsoleRenderedSegment(semantic.Info ?? "Info", "stack-console-segment stack-console-segment--column-heading"),
            ]);
    }

    private static StackConsoleRenderedLine RenderSummaryResourceLine(string key, string line, StackSummaryLineSemantic semantic)
    {
        var segments = new List<StackConsoleRenderedSegment>
        {
            new("     ", "stack-console-segment stack-console-segment--whitespace")
        };

        if (!string.IsNullOrEmpty(semantic.Tree))
        {
            var treeCss = semantic.Tree.Length == 2
                ? "stack-console-segment stack-console-segment--marker"
                : "stack-console-segment stack-console-segment--tree";
            segments.Add(new StackConsoleRenderedSegment(semantic.Tree, treeCss));
        }

        segments.Add(new StackConsoleRenderedSegment(semantic.TypeText ?? string.Empty, "stack-console-segment stack-console-segment--summary-type"));
        segments.Add(new StackConsoleRenderedSegment(semantic.Name ?? string.Empty, "stack-console-segment stack-console-segment--summary-name"));
        segments.Add(new StackConsoleRenderedSegment(semantic.Status ?? string.Empty, "stack-console-segment stack-console-segment--summary-status"));
        segments.Add(new StackConsoleRenderedSegment(semantic.Info ?? string.Empty, "stack-console-segment stack-console-segment--summary-info"));

        return new StackConsoleRenderedLine(key, line, "stack-console-line stack-console-line--summary-grid stack-console-line--summary-row", segments);
    }

    private static IReadOnlyList<StackConsoleRenderedSegment> RenderSummaryTypeColumn(string typeColumn)
    {
        var segments = new List<StackConsoleRenderedSegment>();
        var remainder = typeColumn;

        if (remainder.StartsWith("├─ ", StringComparison.Ordinal) || remainder.StartsWith("└─ ", StringComparison.Ordinal))
        {
            segments.Add(new StackConsoleRenderedSegment(remainder[..3], "stack-console-segment stack-console-segment--tree"));
            remainder = remainder[3..];
        }
        else if (remainder.StartsWith("+ ", StringComparison.Ordinal) ||
                 remainder.StartsWith("- ", StringComparison.Ordinal) ||
                 remainder.StartsWith("~ ", StringComparison.Ordinal) ||
                 remainder.StartsWith("= ", StringComparison.Ordinal))
        {
            segments.Add(new StackConsoleRenderedSegment(remainder[..2], "stack-console-segment stack-console-segment--marker"));
            remainder = remainder[2..];
        }

        segments.Add(new StackConsoleRenderedSegment(remainder, "stack-console-segment stack-console-segment--summary-type"));
        return segments;
    }

    private static StackConsoleRenderedLine RenderDiffLine(string key, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return PlainLine(key, line, "stack-console-line stack-console-line--blank");
        }

        if (line is "Configuration:")
        {
            return HeadingWithRemainder(key, line);
        }

        if (line.Contains("--outputs:--", StringComparison.Ordinal))
        {
            return PlainLine(key, line, "stack-console-line stack-console-line--section-marker", [
                new StackConsoleRenderedSegment(line, "stack-console-segment stack-console-segment--section-marker")
            ]);
        }

        if (line.Contains(": (", StringComparison.Ordinal) && !line.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return RenderDiffResourceLine(key, line);
        }

        if (line.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            var bracketIndex = line.IndexOf('[', StringComparison.Ordinal);
            return PlainLine(key, line, "stack-console-line stack-console-line--property", [
                new StackConsoleRenderedSegment(line[..bracketIndex], "stack-console-segment stack-console-segment--indent"),
                new StackConsoleRenderedSegment(line[bracketIndex..], "stack-console-segment stack-console-segment--property-value")
            ]);
        }

        return RenderIndentedDiffLikeLine(key, line, "stack-console-line stack-console-line--property");
    }

    private static StackConsoleRenderedLine RenderDiffResourceLine(string key, string line)
    {
        var indentLength = line.TakeWhile(static ch => ch == ' ').Count();
        var indent = line[..indentLength];
        var content = line[indentLength..];

        var segments = new List<StackConsoleRenderedSegment>
        {
            new(indent, "stack-console-segment stack-console-segment--indent")
        };

        if (content.Length >= 2 && IsMarkerPrefix(content[..2]))
        {
            var marker = content[..2];
            var variantCss = MarkerVariantCss(marker);
            segments.Add(new StackConsoleRenderedSegment(marker, $"stack-console-segment stack-console-segment--marker {variantCss}".TrimEnd()));
            content = content[2..];
        }

        var split = content.LastIndexOf(": (", StringComparison.Ordinal);
        if (split >= 0)
        {
            var variantCss = MarkerVariantCss(content);
            segments.Add(new StackConsoleRenderedSegment(content[..(split + 1)], $"stack-console-segment stack-console-segment--resource {variantCss}".TrimEnd()));
            segments.Add(new StackConsoleRenderedSegment(content[(split + 1)..], $"stack-console-segment stack-console-segment--status {variantCss}".TrimEnd()));
        }
        else
        {
            segments.Add(new StackConsoleRenderedSegment(content, "stack-console-segment stack-console-segment--resource"));
        }

        return new StackConsoleRenderedLine(key, line, "stack-console-line stack-console-line--resource", segments);
    }

    private static StackConsoleRenderedLine RenderDiagnosticLine(string key, string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return PlainLine(key, line, "stack-console-line stack-console-line--blank");
        }

        if (line is "Diagnostics:")
        {
            return HeadingWithRemainder(key, line);
        }

        if (line.StartsWith("  pulumi:", StringComparison.Ordinal))
        {
            return PlainLine(key, line, "stack-console-line stack-console-line--resource", [
                new StackConsoleRenderedSegment(line[..2], "stack-console-segment stack-console-segment--indent"),
                new StackConsoleRenderedSegment(line[2..], "stack-console-segment stack-console-segment--resource")
            ]);
        }

        if (line.TrimStart().EndsWith(':'))
        {
            var indent = line[..line.IndexOf(line.TrimStart(), StringComparison.Ordinal)];
            return PlainLine(key, line, "stack-console-line stack-console-line--heading", [
                new StackConsoleRenderedSegment(indent, "stack-console-segment stack-console-segment--indent"),
                new StackConsoleRenderedSegment(line.TrimStart(), "stack-console-segment stack-console-segment--heading")
            ]);
        }

        var bodyIndent = line[..line.IndexOf(line.TrimStart(), StringComparison.Ordinal)];
        return PlainLine(key, line, "stack-console-line stack-console-line--diagnostic", [
            new StackConsoleRenderedSegment(bodyIndent, "stack-console-segment stack-console-segment--indent"),
            new StackConsoleRenderedSegment(line.TrimStart(), "stack-console-segment stack-console-segment--diagnostic-text")
        ]);
    }

    private static StackConsoleRenderedLine RenderIndentedDiffLikeLine(string key, string line, string lineCssClass)
    {
        var normalizedLine = line.Replace(UnknownSignatureValue, "[unknown]", StringComparison.Ordinal);
        var indentLength = normalizedLine.TakeWhile(static ch => ch == ' ').Count();
        var indent = normalizedLine[..indentLength];
        var content = normalizedLine[indentLength..];

        var segments = new List<StackConsoleRenderedSegment>
        {
            new(indent, "stack-console-segment stack-console-segment--indent")
        };

        string? marker = null;
        if (content.Length >= 2 && IsMarkerPrefix(content[..2]))
        {
            marker = content[..1];
            segments.Add(new StackConsoleRenderedSegment(marker, $"stack-console-segment stack-console-segment--marker {MarkerVariantCss(marker)}".TrimEnd()));
            segments.Add(new StackConsoleRenderedSegment(" ", "stack-console-segment stack-console-segment--whitespace"));
            content = content[2..];
        }

        var match = DiffPropertyPattern().Match(content);
        if (!match.Success)
        {
            segments.Add(new StackConsoleRenderedSegment(content, "stack-console-segment stack-console-segment--diagnostic-text"));
            return new StackConsoleRenderedLine(key, normalizedLine, lineCssClass, segments);
        }

        segments.Add(new StackConsoleRenderedSegment(match.Groups["key"].Value, "stack-console-segment stack-console-segment--property-key"));
        segments.Add(new StackConsoleRenderedSegment(match.Groups["separator"].Value, "stack-console-segment stack-console-segment--separator"));
        segments.Add(new StackConsoleRenderedSegment(
            match.Groups["value"].Value,
            $"stack-console-segment stack-console-segment--property-value {MarkerVariantCss(marker)}".TrimEnd()));
        return new StackConsoleRenderedLine(key, normalizedLine, lineCssClass, segments);
    }

    private static StackConsoleRenderedLine HeadingWithRemainder(string key, string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || colon == line.Length - 1)
        {
            return PlainLine(key, line, "stack-console-line stack-console-line--heading", [
                new StackConsoleRenderedSegment(line, "stack-console-segment stack-console-segment--heading")
            ]);
        }

        return new StackConsoleRenderedLine(
            key,
            line,
            "stack-console-line stack-console-line--heading",
            [
                new StackConsoleRenderedSegment(line[..(colon + 1)], "stack-console-segment stack-console-segment--heading"),
                new StackConsoleRenderedSegment(line[(colon + 1)..], "stack-console-segment stack-console-segment--heading-detail")
            ]);
    }

    private static bool IsMarkerPrefix(string text)
        => text is "+ " or "- " or "~ " or "= ";

    private static string MarkerVariantCss(string? markerOrLine)
    {
        if (string.IsNullOrWhiteSpace(markerOrLine))
        {
            return string.Empty;
        }

        if (markerOrLine.StartsWith("-", StringComparison.Ordinal))
        {
            return "stack-console-segment--marker-delete stack-console-segment--delete";
        }

        if (markerOrLine.StartsWith("+", StringComparison.Ordinal))
        {
            return "stack-console-segment--marker-create stack-console-segment--create";
        }

        if (markerOrLine.StartsWith("~", StringComparison.Ordinal))
        {
            return "stack-console-segment--marker-update stack-console-segment--update";
        }

        return string.Empty;
    }

    private static StackConsoleRenderedLine PlainLine(
        string key,
        string line,
        string cssClass,
        IReadOnlyList<StackConsoleRenderedSegment>? segments = null)
        => new(key, line, cssClass, segments ?? [new StackConsoleRenderedSegment(line, "stack-console-segment")]);

    [GeneratedRegex("^(?<key>[^:]+?)(?<separator>\\s*:\\s*)(?<value>.*)$", RegexOptions.Compiled)]
    private static partial Regex DiffPropertyPattern();
}
