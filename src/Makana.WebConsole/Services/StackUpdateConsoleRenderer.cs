using System.Text.RegularExpressions;
using Makana.WebConsole.Models;

namespace Makana.WebConsole.Services;

public static partial class StackUpdateConsoleRenderer
{
    public static StackConsoleDocument Render(StackConsoleViewKind viewKind, string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n', StringSplitOptions.None)
            .Select(line => RenderLine(viewKind, line))
            .ToArray();

        return new StackConsoleDocument(viewKind, lines);
    }

    public static string Flatten(StackConsoleDocument document)
        => string.Join('\n', document.Lines.Select(line => string.Concat(line.Segments.Select(segment => segment.Text))));

    private static StackConsoleLine RenderLine(StackConsoleViewKind viewKind, string line)
        => viewKind switch
        {
            StackConsoleViewKind.Summary => RenderSummaryLine(line),
            StackConsoleViewKind.Diff => RenderDiffLine(line),
            StackConsoleViewKind.Diagnostic => RenderDiagnosticLine(line),
            _ => PlainLine(line, "stack-console-line")
        };

    private static StackConsoleLine RenderSummaryLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return PlainLine(line, "stack-console-line stack-console-line--blank");
        }

        if (line.StartsWith("     Type", StringComparison.Ordinal))
        {
            return new StackConsoleLine(
                line,
                "stack-console-line stack-console-line--header",
                [
                    new StackConsoleSegment(line[..5], "stack-console-segment stack-console-segment--whitespace"),
                    new StackConsoleSegment(line[5..28], "stack-console-segment stack-console-segment--column-heading"),
                    new StackConsoleSegment(line[28..58], "stack-console-segment stack-console-segment--column-heading"),
                    new StackConsoleSegment(line[58..69], "stack-console-segment stack-console-segment--column-heading"),
                    new StackConsoleSegment(line[69..], "stack-console-segment stack-console-segment--column-heading"),
                ]);
        }

        if (line is "Outputs:" or "Resources:" || line.StartsWith("Duration:", StringComparison.Ordinal))
        {
            return HeadingWithRemainder(line);
        }

        if (line.StartsWith("     ", StringComparison.Ordinal) && line.Length >= 69)
        {
            var typeColumn = line[5..28];
            var nameColumn = line[28..58];
            var statusColumn = line[58..69];
            var infoColumn = line.Length > 69 ? line[69..] : string.Empty;

            var typeSegments = RenderSummaryTypeColumn(typeColumn);
            var segments = new List<StackConsoleSegment>(typeSegments.Count + 4)
            {
                new StackConsoleSegment(line[..5], "stack-console-segment stack-console-segment--whitespace")
            };
            segments.AddRange(typeSegments);
            segments.Add(new StackConsoleSegment(nameColumn, "stack-console-segment stack-console-segment--summary-name"));
            segments.Add(new StackConsoleSegment(statusColumn, "stack-console-segment stack-console-segment--summary-status"));
            segments.Add(new StackConsoleSegment(infoColumn, "stack-console-segment stack-console-segment--summary-info"));
            return new StackConsoleLine(line, "stack-console-line stack-console-line--summary-row", segments);
        }

        return RenderIndentedDiffLikeLine(line, "stack-console-line stack-console-line--summary-detail");
    }

    private static IReadOnlyList<StackConsoleSegment> RenderSummaryTypeColumn(string typeColumn)
    {
        var segments = new List<StackConsoleSegment>();
        var remainder = typeColumn;

        if (remainder.StartsWith("├─ ", StringComparison.Ordinal) || remainder.StartsWith("└─ ", StringComparison.Ordinal))
        {
            segments.Add(new StackConsoleSegment(remainder[..3], "stack-console-segment stack-console-segment--tree"));
            remainder = remainder[3..];
        }
        else if (remainder.StartsWith("+ ", StringComparison.Ordinal) ||
                 remainder.StartsWith("- ", StringComparison.Ordinal) ||
                 remainder.StartsWith("~ ", StringComparison.Ordinal) ||
                 remainder.StartsWith("= ", StringComparison.Ordinal))
        {
            segments.Add(new StackConsoleSegment(remainder[..2], "stack-console-segment stack-console-segment--marker"));
            remainder = remainder[2..];
        }

        segments.Add(new StackConsoleSegment(remainder, "stack-console-segment stack-console-segment--summary-type"));
        return segments;
    }

    private static StackConsoleLine RenderDiffLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return PlainLine(line, "stack-console-line stack-console-line--blank");
        }

        if (line is "Configuration:")
        {
            return HeadingWithRemainder(line);
        }

        if (line.Contains("--outputs:--", StringComparison.Ordinal))
        {
            return PlainLine(line, "stack-console-line stack-console-line--section-marker", [
                new StackConsoleSegment(line, "stack-console-segment stack-console-segment--section-marker")
            ]);
        }

        if (line.Contains(": (", StringComparison.Ordinal) && !line.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return RenderDiffResourceLine(line);
        }

        if (line.TrimStart().StartsWith("[", StringComparison.Ordinal))
        {
            return PlainLine(line, "stack-console-line stack-console-line--property", [
                new StackConsoleSegment(line[..line.IndexOf('[', StringComparison.Ordinal)], "stack-console-segment stack-console-segment--indent"),
                new StackConsoleSegment(line[line.IndexOf('[', StringComparison.Ordinal)..], "stack-console-segment stack-console-segment--property-value")
            ]);
        }

        return RenderIndentedDiffLikeLine(line, "stack-console-line stack-console-line--property");
    }

    private static StackConsoleLine RenderDiffResourceLine(string line)
    {
        var indentLength = line.TakeWhile(ch => ch == ' ').Count();
        var indent = line[..indentLength];
        var content = line[indentLength..];
        var segments = new List<StackConsoleSegment>
        {
            new(indent, "stack-console-segment stack-console-segment--indent")
        };

        if (content.StartsWith("+ ", StringComparison.Ordinal) ||
            content.StartsWith("- ", StringComparison.Ordinal) ||
            content.StartsWith("~ ", StringComparison.Ordinal) ||
            content.StartsWith("= ", StringComparison.Ordinal))
        {
            segments.Add(new StackConsoleSegment(content[..2], "stack-console-segment stack-console-segment--marker"));
            content = content[2..];
        }

        var split = content.IndexOf(": (", StringComparison.Ordinal);
        if (split >= 0)
        {
            segments.Add(new StackConsoleSegment(content[..(split + 1)], "stack-console-segment stack-console-segment--resource"));
            segments.Add(new StackConsoleSegment(content[(split + 1)..], "stack-console-segment stack-console-segment--status"));
        }
        else
        {
            segments.Add(new StackConsoleSegment(content, "stack-console-segment stack-console-segment--resource"));
        }

        return new StackConsoleLine(line, "stack-console-line stack-console-line--resource", segments);
    }

    private static StackConsoleLine RenderDiagnosticLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return PlainLine(line, "stack-console-line stack-console-line--blank");
        }

        if (line is "Diagnostics:")
        {
            return HeadingWithRemainder(line);
        }

        if (line.Contains("pulumi:pulumi:Stack", StringComparison.Ordinal))
        {
            return PlainLine(line, "stack-console-line stack-console-line--resource", [
                new StackConsoleSegment(line[..2], "stack-console-segment stack-console-segment--indent"),
                new StackConsoleSegment(line[2..], "stack-console-segment stack-console-segment--resource")
            ]);
        }

        if (line.TrimStart().EndsWith(":", StringComparison.Ordinal))
        {
            return PlainLine(line, "stack-console-line stack-console-line--heading", [
                new StackConsoleSegment(line[..line.IndexOf(line.TrimStart(), StringComparison.Ordinal)], "stack-console-segment stack-console-segment--indent"),
                new StackConsoleSegment(line.TrimStart(), "stack-console-segment stack-console-segment--heading")
            ]);
        }

        return PlainLine(line, "stack-console-line stack-console-line--diagnostic", [
            new StackConsoleSegment(line[..line.IndexOf(line.TrimStart(), StringComparison.Ordinal)], "stack-console-segment stack-console-segment--indent"),
            new StackConsoleSegment(line.TrimStart(), "stack-console-segment stack-console-segment--diagnostic-text")
        ]);
    }

    private static StackConsoleLine RenderIndentedDiffLikeLine(string line, string lineCssClass)
    {
        var match = PropertyLineRegex().Match(line);
        if (!match.Success)
        {
            return PlainLine(line, lineCssClass);
        }

        var segments = new List<StackConsoleSegment>
        {
            new(match.Groups["indent"].Value, "stack-console-segment stack-console-segment--indent")
        };

        var marker = match.Groups["marker"].Value;
        if (!string.IsNullOrEmpty(marker))
        {
            segments.Add(new StackConsoleSegment(marker, "stack-console-segment stack-console-segment--marker"));
            segments.Add(new StackConsoleSegment(" ", "stack-console-segment stack-console-segment--whitespace"));
        }

        segments.Add(new StackConsoleSegment(match.Groups["key"].Value, "stack-console-segment stack-console-segment--property-key"));
        segments.Add(new StackConsoleSegment(match.Groups["separator"].Value, "stack-console-segment stack-console-segment--separator"));
        segments.Add(new StackConsoleSegment(match.Groups["value"].Value, "stack-console-segment stack-console-segment--property-value"));
        return new StackConsoleLine(line, lineCssClass, segments);
    }

    private static StackConsoleLine HeadingWithRemainder(string line)
    {
        var colon = line.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return PlainLine(line, "stack-console-line stack-console-line--heading");
        }

        return new StackConsoleLine(
            line,
            "stack-console-line stack-console-line--heading",
            [
                new StackConsoleSegment(line[..(colon + 1)], "stack-console-segment stack-console-segment--heading"),
                new StackConsoleSegment(line[(colon + 1)..], "stack-console-segment stack-console-segment--heading-detail")
            ]);
    }

    private static StackConsoleLine PlainLine(string line, string cssClass, IReadOnlyList<StackConsoleSegment>? segments = null)
        => new(line, cssClass, segments ?? [new StackConsoleSegment(line, "stack-console-segment")]);

    [GeneratedRegex(@"^(?<indent>\s*)(?:(?<marker>\+|-|~)\s)?(?<key>[^:]+?)(?<separator>\s*:\s*)(?<value>.*)$", RegexOptions.Compiled)]
    private static partial Regex PropertyLineRegex();
}
