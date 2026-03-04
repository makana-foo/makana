using System.Text;
using System.Text.Json;
using Makana.WebConsole.Models;

namespace Makana.WebConsole.Services;

public static class StackUpdateTextFormatter
{
    public static string BuildDiagnosticTranscript(
        IReadOnlyList<StackTranscriptRow> rows,
        string owner,
        string project,
        string stack)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Diagnostics:");
        builder.Append("  pulumi:pulumi:Stack (");
        builder.Append(GetRootStack(rows)?.ResourceName ?? $"{project}-{stack}");
        builder.AppendLine("):");

        var configuration = rows.OfType<StackConfigurationTranscriptRow>().FirstOrDefault();
        if (configuration is not null)
        {
            builder.AppendLine("    Configuration:");
            foreach (var entry in configuration.Entries)
            {
                builder.Append("        ");
                builder.Append(entry.Key);
                builder.Append(": ");
                builder.AppendLine(entry.Value);
            }
        }

        foreach (var row in rows)
        {
            switch (row)
            {
                case StackDiagnosticTranscriptRow diagnostic:
                    foreach (var line in diagnostic.Message.Split('\n', StringSplitOptions.None))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        builder.Append("    ");
                        builder.AppendLine(line);
                    }

                    break;

                case StackTextTranscriptRow text:
                    foreach (var line in text.Text.Split('\n', StringSplitOptions.None))
                    {
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        builder.Append("    ");
                        builder.AppendLine(line);
                    }

                    break;
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string BuildSummaryTranscript(
        IReadOnlyList<StackResourceTranscriptRow> resources,
        IReadOnlyList<StackTranscriptRow> rows)
    {
        if (resources.Count == 0)
        {
            return "No changes.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("     Type                   Name                          Status     Info");

        var orderedResources = resources.ToArray();
        var root = GetRootStack(rows);
        var siblingInfo = BuildSiblingInfo(orderedResources);
        var messageCount = CountSummaryMessages(rows);

        foreach (var resource in orderedResources)
        {
            var typeColumn = BuildSummaryTypeColumn(resource, siblingInfo);
            var status = string.Equals(resource.Operation, "same", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : resource.Status ?? string.Empty;
            var info = root is not null && ReferenceEqualsByIdentity(root, resource) && messageCount > 0
                ? $"{messageCount} message"
                : string.Empty;

            builder.Append("     ");
            builder.Append(typeColumn.PadRight(23));
            builder.Append((resource.ResourceName ?? "resource").PadRight(30));
            builder.Append(status.PadRight(11));
            builder.Append(info);

            builder.AppendLine();
        }

        var outputLines = BuildSummaryOutputLines(root);
        if (outputLines.Count > 0)
        {
            builder.AppendLine("Outputs:");
            foreach (var line in outputLines)
            {
                builder.AppendLine(line);
            }
        }

        var summary = rows.OfType<StackSummaryTranscriptRow>().LastOrDefault();
        if (summary is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Resources:");
            foreach (var line in BuildResourceSummaryLines(summary))
            {
                builder.AppendLine(line);
            }

            var duration = ExtractDuration(summary);
            if (duration is not null)
            {
                builder.AppendLine();
                builder.Append("Duration: ");
                builder.AppendLine(duration);
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static IReadOnlyList<string> BuildDiffLines(IReadOnlyList<StackTranscriptRow> rows)
    {
        var lines = new List<string>();
        var configuration = rows.OfType<StackConfigurationTranscriptRow>().FirstOrDefault();
        var resources = rows.OfType<StackResourceTranscriptRow>().ToArray();
        var root = GetRootStack(rows);
        if (root is null)
        {
            return [];
        }

        lines.Add("Configuration:");
        if (configuration is not null)
        {
            foreach (var entry in configuration.Entries)
            {
                lines.Add($"    {entry.Key}: {entry.Value}");
            }
        }

        lines.Add($"  {FormatDiffResourceHeader(root)}");
        if (!string.IsNullOrWhiteSpace(root.ResourceUrn))
        {
            lines.Add($"    [urn={root.ResourceUrn}]");
        }

        var state = GetStackState(root);
        if (state.oldOutputs.ValueKind == JsonValueKind.Object && state.oldOutputs.EnumerateObject().Any())
        {
            lines.Add("    --outputs:--");
            lines.AddRange(RenderObjectForDiff(state.oldOutputs, 4));
        }

        foreach (var child in resources.Where(x => string.Equals(x.ParentUrn, root.ResourceUrn, StringComparison.Ordinal)))
        {
            lines.Add($"      {FormatDiffResourceHeader(child)}");
            if (!string.IsNullOrWhiteSpace(child.ResourceUrn))
            {
                lines.Add($"        [urn={child.ResourceUrn}]");
            }
        }

        if (state.newOutputs.ValueKind == JsonValueKind.Object && state.newOutputs.EnumerateObject().Any())
        {
            lines.Add("    --outputs:--");
            lines.AddRange(RenderDiffObject(state.oldOutputs, state.newOutputs, 2, includeInternalMarkers: false));
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildResourceSummaryLines(StackSummaryTranscriptRow summary)
    {
        using var document = JsonDocument.Parse(summary.RawPayloadJson);
        if (!document.RootElement.TryGetProperty("summaryEvent", out var summaryEvent) ||
            !summaryEvent.TryGetProperty("resourceChanges", out var changes) ||
            changes.ValueKind != JsonValueKind.Object)
        {
            return ["    " + summary.Summary];
        }

        var lines = new List<string>();
        foreach (var change in changes.EnumerateObject())
        {
            if (change.Value.ValueKind != JsonValueKind.Number || !change.Value.TryGetInt32(out var count) || count <= 0)
            {
                continue;
            }

            var marker = MarkerForSummaryChange(change.Name);
            var label = string.Equals(change.Name, "same", StringComparison.Ordinal) ? "unchanged" : change.Name;
            lines.Add(string.IsNullOrEmpty(marker)
                ? $"    {count} {label}"
                : $"    {marker} {count} {label}");
        }

        return lines.Count == 0 ? ["    " + summary.Summary] : lines;
    }

    private static string? ExtractDuration(StackSummaryTranscriptRow summary)
    {
        using var document = JsonDocument.Parse(summary.RawPayloadJson);
        if (!document.RootElement.TryGetProperty("summaryEvent", out var summaryEvent) ||
            !summaryEvent.TryGetProperty("durationSeconds", out var duration) ||
            duration.ValueKind != JsonValueKind.Number ||
            !duration.TryGetInt64(out var seconds))
        {
            return null;
        }

        return FormatDuration(TimeSpan.FromSeconds(seconds));
    }

    private static IReadOnlyList<string> BuildSummaryOutputLines(StackResourceTranscriptRow? root)
    {
        if (root is null)
        {
            return [];
        }

        var state = GetStackState(root);
        if (state.newOutputs.ValueKind != JsonValueKind.Object || !state.newOutputs.EnumerateObject().Any())
        {
            return [];
        }

        return RenderDiffObject(state.oldOutputs, state.newOutputs, 2, includeInternalMarkers: true);
    }

    private static (JsonElement oldOutputs, JsonElement newOutputs) GetStackState(StackResourceTranscriptRow root)
    {
        using var document = JsonDocument.Parse(root.RawPayloadJson);
        if (!document.RootElement.TryGetProperty("resOutputsEvent", out var outputsEvent) ||
            !outputsEvent.TryGetProperty("metadata", out var metadata))
        {
            return (default, default);
        }

        var oldOutputs = TryGetNestedOutputs(metadata, "old");
        var newOutputs = TryGetNestedOutputs(metadata, "new");
        return (oldOutputs, newOutputs);
    }

    private static JsonElement TryGetNestedOutputs(JsonElement metadata, string stateName)
    {
        if (!metadata.TryGetProperty(stateName, out var state) ||
            state.ValueKind != JsonValueKind.Object ||
            !state.TryGetProperty("outputs", out var outputs))
        {
            return default;
        }

        return outputs.Clone();
    }

    private static List<string> RenderDiffObject(
        JsonElement oldValue,
        JsonElement newValue,
        int indent,
        bool includeInternalMarkers)
    {
        var lines = new List<string>();
        if (newValue.ValueKind != JsonValueKind.Object)
        {
            return lines;
        }

        var properties = newValue.EnumerateObject()
            .Where(x => includeInternalMarkers || !string.Equals(x.Name, "@isPulumiResource", StringComparison.Ordinal))
            .ToArray();
        var oldProperties = oldValue.ValueKind == JsonValueKind.Object
            ? oldValue.EnumerateObject().ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var labelWidth = properties.Length == 0 ? 0 : properties.Max(x => x.Name.Length);

        foreach (var property in properties)
        {
            oldProperties.TryGetValue(property.Name, out var oldProperty);
            var status = DetermineDiffStatus(oldProperty, property.Value);
            var lineIndent = new string(' ', indent);

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var marker = status == ' ' ? "~" : status.ToString();
                lines.Add($"{lineIndent}{marker} {FormatPropertyLabel(property.Name, labelWidth, indent)}: {{");
                lines.AddRange(RenderDiffObject(oldProperty, property.Value, indent + 4, includeInternalMarkers));
                lines.Add($"{new string(' ', indent + 2)}}}");
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                lines.Add($"{lineIndent}{(status == ' ' ? " " : status.ToString())} {FormatPropertyLabel(property.Name, labelWidth, indent)}: {FormatJson(property.Value)}");
                continue;
            }

            var prefix = status == ' ' ? "  " : $"{status} ";
            lines.Add($"{lineIndent}{prefix}{FormatPropertyLabel(property.Name, labelWidth, indent)}: {FormatJson(property.Value)}");
        }

        return lines;
    }

    private static IReadOnlyList<string> RenderObjectForDiff(JsonElement value, int indent)
    {
        var lines = new List<string>();
        if (value.ValueKind != JsonValueKind.Object)
        {
            return lines;
        }

        var properties = value.EnumerateObject().ToArray();
        var labelWidth = properties.Length == 0 ? 0 : properties.Max(x => x.Name.Length);
        foreach (var property in properties)
        {
            var lineIndent = new string(' ', indent);
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                lines.Add($"{lineIndent}{property.Name}: {{");
                lines.AddRange(RenderObjectForDiff(property.Value, indent + 4));
                lines.Add($"{lineIndent}}}");
            }
            else
            {
                lines.Add($"{lineIndent}{FormatPropertyLabel(property.Name, labelWidth, indent)}: {FormatJson(property.Value)}");
            }
        }

        return lines;
    }

    private static char DetermineDiffStatus(JsonElement oldValue, JsonElement newValue)
    {
        if (oldValue.ValueKind == JsonValueKind.Undefined)
        {
            return '+';
        }

        if (JsonElementEquals(oldValue, newValue))
        {
            return ' ';
        }

        return '~';
    }

    private static bool JsonElementEquals(JsonElement left, JsonElement right)
        => left.ValueKind == right.ValueKind && left.GetRawText() == right.GetRawText();

    private static string FormatJson(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => $"\"{value.GetString()}\"",
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null => "<null>",
            JsonValueKind.Array => value.GetRawText(),
            JsonValueKind.Object => value.GetRawText(),
            _ => value.GetRawText()
        };
    }

    private static string FormatPropertyLabel(string key, int width, int indent)
    {
        if (width <= key.Length || indent <= 2)
        {
            return key;
        }

        return key.PadRight(width);
    }

    private static Dictionary<string, (int index, int count)> BuildSiblingInfo(IReadOnlyList<StackResourceTranscriptRow> resources)
    {
        var info = new Dictionary<string, (int index, int count)>(StringComparer.Ordinal);
        foreach (var group in resources.Where(x => !string.IsNullOrWhiteSpace(x.ParentUrn)).GroupBy(x => x.ParentUrn!, StringComparer.Ordinal))
        {
            var siblings = group.ToArray();
            for (var i = 0; i < siblings.Length; i++)
            {
                if (siblings[i].ResourceUrn is { } urn)
                {
                    info[urn] = (i, siblings.Length);
                }
            }
        }

        return info;
    }

    private static string BuildSummaryTypeColumn(StackResourceTranscriptRow resource, IReadOnlyDictionary<string, (int index, int count)> siblingInfo)
    {
        var type = resource.ResourceType ?? "resource";
        if (string.IsNullOrWhiteSpace(resource.ParentUrn) || resource.ResourceUrn is null || !siblingInfo.TryGetValue(resource.ResourceUrn, out var sibling))
        {
            return type;
        }

        var connector = sibling.index == sibling.count - 1 ? "└─ " : "├─ ";
        return connector + type;
    }

    private static int CountSummaryMessages(IReadOnlyList<StackTranscriptRow> rows)
    {
        var count = rows.OfType<StackConfigurationTranscriptRow>().Count();
        count += rows.OfType<StackDiagnosticTranscriptRow>().Count();
        return count;
    }

    private static StackResourceTranscriptRow? GetRootStack(IReadOnlyList<StackTranscriptRow> rows)
    {
        return rows.OfType<StackResourceTranscriptRow>()
            .FirstOrDefault(x => string.Equals(x.ResourceType, "pulumi:pulumi:Stack", StringComparison.Ordinal));
    }

    private static bool ReferenceEqualsByIdentity(StackResourceTranscriptRow left, StackResourceTranscriptRow right)
        => string.Equals(left.ResourceUrn, right.ResourceUrn, StringComparison.Ordinal);

    private static string FormatDiffResourceHeader(StackResourceTranscriptRow resource)
    {
        var op = resource.Operation.ToLowerInvariant();
        return string.Equals(op, "same", StringComparison.Ordinal)
            ? $"{resource.ResourceType}: ({op})"
            : $"{resource.Marker} {resource.ResourceType}: ({op})";
    }

    private static string MarkerForSummaryChange(string change)
    {
        return change switch
        {
            "create" => "+",
            "delete" => "-",
            "replace" => "+-",
            "update" => "~",
            _ => string.Empty
        };
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h{duration.Minutes}m{duration.Seconds}s";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes}m{duration.Seconds}s";
        }

        return $"{duration.Seconds}s";
    }
}
