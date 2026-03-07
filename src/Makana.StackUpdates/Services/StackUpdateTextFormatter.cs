using System.Text;
using System.Text.Json;
using Makana.StackUpdates.Models;
using Makana.StackUpdates.Pulumi;

namespace Makana.StackUpdates.Services;

public static class StackUpdateTextFormatter
{
    private const string PulumiSignatureKey = "4dabf18193072939515e22adb298388d";
    private const string SecretSignatureValue = "1b47061264138c4ac30d75fd1eb44270";
    private const string UnknownSignatureValue = "04da6b54-80e4-46f7-96ec-b56ff0331ba9";

    public static IReadOnlyList<StackConsoleLineProjection> BuildSummaryLineProjections(IReadOnlyList<StackTranscriptRow> rows)
    {
        var resources = rows.OfType<StackResourceTranscriptRow>().ToArray();
        var summaryData = BuildSummaryViewData(resources, rows);
        if (summaryData.Resources.Count == 0)
        {
            return [new StackConsoleLineProjection("summary:empty", 0, "No changes.")];
        }

        var lines = new List<StackConsoleLineProjection>
        {
            new("summary:header", 0, "     Type                   Name                          Status     Info")
        };

        for (var index = 0; index < summaryData.Resources.Count; index++)
        {
            var resource = summaryData.Resources[index];
            var row = resources[index];
            var builder = new StringBuilder();
            builder.Append("     ");
            AppendSummaryColumn(builder, resource.TypeText, 23);
            AppendSummaryColumn(builder, resource.Name, 30);
            AppendSummaryColumn(builder, resource.Status, 11);
            builder.Append(resource.Info);
            lines.Add(new StackConsoleLineProjection($"summary:resource:{row.Key}", row.Sequence, builder.ToString()));
        }

        var root = GetRootStack(rows);

        if (summaryData.OutputLines.Count > 0)
        {
            lines.Add(new StackConsoleLineProjection("summary:outputs:header", root?.Sequence ?? 0, "Outputs:"));
            for (var index = 0; index < summaryData.OutputLines.Count; index++)
            {
                lines.Add(new StackConsoleLineProjection(
                    $"summary:outputs:{index}",
                    root?.Sequence ?? 0,
                    summaryData.OutputLines[index]));
            }
        }

        if (summaryData.ResourceSummaryLines.Count > 0)
        {
            lines.Add(new StackConsoleLineProjection("summary:resources:spacer", summaryData.Resources.Count > 0 ? resources[^1].Sequence : 0, string.Empty));
            lines.Add(new StackConsoleLineProjection("summary:resources:header", summaryData.Resources.Count > 0 ? resources[^1].Sequence : 0, "Resources:"));
            for (var index = 0; index < summaryData.ResourceSummaryLines.Count; index++)
            {
                lines.Add(new StackConsoleLineProjection(
                    $"summary:resources:{index}",
                    summaryData.Resources.Count > 0 ? resources[^1].Sequence : 0,
                    summaryData.ResourceSummaryLines[index]));
            }

            if (summaryData.Duration is not null)
            {
                lines.Add(new StackConsoleLineProjection("summary:duration:spacer", summaryData.Resources.Count > 0 ? resources[^1].Sequence : 0, string.Empty));
                lines.Add(new StackConsoleLineProjection("summary:duration", summaryData.Resources.Count > 0 ? resources[^1].Sequence : 0, $"Duration: {summaryData.Duration}"));
            }
        }

        return lines;
    }

    public static IReadOnlyList<StackConsoleLineProjection> BuildDiffLineProjections(IReadOnlyList<StackTranscriptRow> rows)
    {
        var lines = new List<StackConsoleLineProjection>
        {
            new("diff:configuration:header", 0, "Configuration:")
        };

        var configuration = rows.OfType<StackConfigurationTranscriptRow>().FirstOrDefault();
        if (configuration is not null)
        {
            foreach (var entry in configuration.Entries)
            {
                lines.Add(new StackConsoleLineProjection(
                    $"diff:configuration:{entry.Key}",
                    configuration.Sequence,
                    $"    {entry.Key}: {entry.Value}"));
            }
        }

        var resources = rows.OfType<StackResourceTranscriptRow>().ToArray();
        var root = GetRootStack(rows);
        if (root is null && resources.Length == 0)
        {
            return lines;
        }

        var childMap = resources
            .Where(static resource => !string.IsNullOrWhiteSpace(resource.ParentUrn))
            .GroupBy(static resource => resource.ParentUrn!, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);

        if (root is not null && string.Equals(root.ResourceType, "pulumi:pulumi:Stack", StringComparison.Ordinal))
        {
            AppendIndexedLines(lines, $"diff:{root.Key}:header", root.Sequence, BuildDiffResourceHeaderLines(root, 0));

            childMap.TryGetValue(root.ResourceUrn ?? string.Empty, out var children);
            if (children is { Length: > 0 })
            {
                var state = GetStackState(root);
                if (state.oldOutputs is { Count: > 0 })
                {
                    lines.Add(new StackConsoleLineProjection($"diff:{root.Key}:old-outputs:header", root.Sequence, "    --outputs:--"));
                    AppendIndexedLines(lines, $"diff:{root.Key}:old-outputs", root.Sequence, RenderObjectForDiff(state.oldOutputs, 4));
                }

                foreach (var child in children)
                {
                    AppendDiffResourceLines(lines, child, 1, childMap);
                }

                if (state.newOutputs is { Count: > 0 })
                {
                    lines.Add(new StackConsoleLineProjection($"diff:{root.Key}:new-outputs:header", root.Sequence, "    --outputs:--"));
                    var outputLines = string.Equals(root.Operation, "create", StringComparison.OrdinalIgnoreCase) ||
                                      string.Equals(root.Operation, "import", StringComparison.OrdinalIgnoreCase)
                        ? RenderObjectForDiff(state.newOutputs, 4)
                        : RenderDiffObject(state.oldOutputs, state.newOutputs, 2, includeInternalMarkers: false);
                    AppendIndexedLines(lines, $"diff:{root.Key}:new-outputs", root.Sequence, outputLines);
                }
            }
            else
            {
                var state = GetStackState(root);
                var detailLines = (string.Equals(root.Operation, "same", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(root.Operation, "update", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(root.Operation, "replace", StringComparison.OrdinalIgnoreCase)) &&
                                  state.newOutputs is { Count: > 0 }
                    ? RenderDetailedDiffObject(FilterInternalProperties(state.oldOutputs), FilterInternalProperties(state.newOutputs), 2)
                    : BuildResourcePropertyDetails(root, 0);
                AppendIndexedLines(lines, $"diff:{root.Key}:detail", root.Sequence, detailLines);
            }
        }
        else
        {
            var topLevelResources = root is not null
                ? [root]
                : resources
                    .Where(static resource => string.IsNullOrWhiteSpace(resource.ParentUrn))
                    .OrderBy(static resource => resource.Sequence)
                    .ToArray();

            foreach (var resource in topLevelResources)
            {
                AppendDiffResourceLines(lines, resource, 0, childMap);
            }
        }

        return lines;
    }

    public static IReadOnlyList<StackConsoleLineProjection> BuildDiagnosticLineProjections(
        IReadOnlyList<StackTranscriptRow> rows,
        string owner,
        string project,
        string stack)
    {
        var lines = new List<StackConsoleLineProjection>
        {
            new("diagnostic:header", 0, "Diagnostics:")
        };

        var root = GetRootStack(rows);
        lines.Add(new StackConsoleLineProjection(
            "diagnostic:stack",
            root?.Sequence ?? 0,
            $"  pulumi:pulumi:Stack ({root?.ResourceName ?? $"{project}-{stack}"}):"));

        var configuration = rows.OfType<StackConfigurationTranscriptRow>().FirstOrDefault();
        if (configuration is not null)
        {
            lines.Add(new StackConsoleLineProjection("diagnostic:configuration:header", configuration.Sequence, "    Configuration:"));
            foreach (var entry in configuration.Entries)
            {
                lines.Add(new StackConsoleLineProjection(
                    $"diagnostic:configuration:{entry.Key}",
                    configuration.Sequence,
                    $"        {entry.Key}: {entry.Value}"));
            }
        }

        foreach (var row in rows)
        {
            switch (row)
            {
                case StackDiagnosticTranscriptRow diagnostic:
                    AppendDiagnosticMessageLines(lines, $"diagnostic:{diagnostic.Key}", diagnostic.Sequence, diagnostic.Message);
                    break;

                case StackTextTranscriptRow text:
                    AppendDiagnosticMessageLines(lines, $"diagnostic:{text.Key}", text.Sequence, text.Text);
                    break;
            }
        }

        return lines;
    }

    public static StackSummaryViewData BuildSummaryViewData(
        IReadOnlyList<StackResourceTranscriptRow> resources,
        IReadOnlyList<StackTranscriptRow> rows)
    {
        if (resources.Count == 0)
        {
            return new StackSummaryViewData([], [], [], null);
        }

        var orderedResources = resources.ToArray();
        var root = GetRootStack(rows);
        var siblingInfo = BuildSiblingInfo(orderedResources);
        var messageCount = CountSummaryMessages(rows);

        var resourceLines = new List<StackSummaryResourceViewData>(orderedResources.Length);
        foreach (var resource in orderedResources)
        {
            var status = string.Equals(resource.Operation, "same", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : resource.Status ?? string.Empty;
            var info = root is not null && ReferenceEqualsByIdentity(root, resource) && messageCount > 0
                ? $"{messageCount} message"
                : string.Empty;

            resourceLines.Add(new StackSummaryResourceViewData(
                BuildSummaryTypeColumn(resource, siblingInfo),
                resource.ResourceName ?? "resource",
                status,
                info));
        }

        var outputLines = BuildSummaryOutputLines(root);
        var summary = rows.OfType<StackSummaryTranscriptRow>().LastOrDefault();
        var resourceSummaryLines = summary is not null ? BuildResourceSummaryLines(summary) : [];
        var duration = summary is not null ? ExtractDuration(summary) : null;

        return new StackSummaryViewData(resourceLines, outputLines, resourceSummaryLines, duration);
    }

    public static string BuildDiagnosticTranscript(
        IReadOnlyList<StackTranscriptRow> rows,
        string owner,
        string project,
        string stack)
    {
        return string.Join('\n', BuildDiagnosticLineProjections(rows, owner, project, stack).Select(static line => line.Text));
    }

    public static string BuildSummaryTranscript(
        IReadOnlyList<StackResourceTranscriptRow> resources,
        IReadOnlyList<StackTranscriptRow> rows)
    {
        return string.Join('\n', BuildSummaryLineProjections(rows).Select(static line => line.Text));
    }

    public static IReadOnlyList<string> BuildDiffLines(IReadOnlyList<StackTranscriptRow> rows)
    {
        return BuildDiffLineProjections(rows).Select(static line => line.Text).ToArray();
    }

    private static void AppendDiffResourceLines(
        List<StackConsoleLineProjection> lines,
        StackResourceTranscriptRow resource,
        int depth,
        IReadOnlyDictionary<string, StackResourceTranscriptRow[]> childMap)
    {
        var rendered = new List<string>();
        AppendDiffResource(rendered, resource, depth, childMap);
        AppendIndexedLines(lines, $"diff:{resource.Key}", resource.Sequence, rendered);
    }

    private static IReadOnlyList<string> BuildDiffResourceHeaderLines(StackResourceTranscriptRow resource, int depth)
    {
        var lines = new List<string>();
        AppendDiffResourceHeader(lines, resource, depth);
        return lines;
    }

    private static void AppendDiagnosticMessageLines(
        List<StackConsoleLineProjection> lines,
        string keyPrefix,
        long sequence,
        string message)
    {
        var index = 0;
        foreach (var line in message.Split('\n', StringSplitOptions.None))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            lines.Add(new StackConsoleLineProjection($"{keyPrefix}:{index++}", sequence, $"    {line}"));
        }
    }

    private static void AppendIndexedLines(
        List<StackConsoleLineProjection> lines,
        string keyPrefix,
        long sequence,
        IReadOnlyList<string> renderedLines)
    {
        for (var index = 0; index < renderedLines.Count; index++)
        {
            lines.Add(new StackConsoleLineProjection($"{keyPrefix}:{index}", sequence, renderedLines[index]));
        }
    }

    private static void AppendDiffResource(
        List<string> lines,
        StackResourceTranscriptRow resource,
        int depth,
        IReadOnlyDictionary<string, StackResourceTranscriptRow[]> childMap)
    {
        AppendDiffResourceHeader(lines, resource, depth);

        lines.AddRange(BuildResourcePropertyDetails(resource, depth));

        if (!string.IsNullOrWhiteSpace(resource.ResourceUrn) &&
            childMap.TryGetValue(resource.ResourceUrn, out var children))
        {
            foreach (var child in children)
            {
                AppendDiffResource(lines, child, depth + 1, childMap);
            }
        }
    }

    private static void AppendDiffResourceHeader(List<string> lines, StackResourceTranscriptRow resource, int depth)
    {
        var indent = new string(' ', depth * 4);
        lines.Add($"{indent}{FormatDiffResourceHeader(resource)}");

        var pseudoIndent = indent + "    ";
        if (!string.IsNullOrWhiteSpace(resource.Metadata.New?.Id ?? resource.Metadata.Old?.Id))
        {
            lines.Add($"{pseudoIndent}[id={resource.Metadata.New?.Id ?? resource.Metadata.Old?.Id}]");
        }

        if (!string.IsNullOrWhiteSpace(resource.ResourceUrn))
        {
            lines.Add($"{pseudoIndent}[urn={resource.ResourceUrn}]");
        }

        var provider = resource.Metadata.Provider;
        if (!string.IsNullOrWhiteSpace(provider) && !IsDefaultProvider(provider))
        {
            lines.Add($"{pseudoIndent}[provider={provider}]");
        }
    }

    private static IReadOnlyList<string> BuildResourcePropertyDetails(StackResourceTranscriptRow resource, int depth)
    {
        if (string.Equals(resource.Operation, "same", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var metadata = resource.Metadata;
        if (metadata.Old is null && metadata.New is not null)
        {
            var createSource = ShouldUseCreateOutputs(metadata.New)
                ? metadata.New.Outputs
                : metadata.New.Inputs;
            return RenderDetailedObject(FilterInternalProperties(createSource), (depth + 1) * 4);
        }

        if (metadata.New is null && metadata.Old is not null)
        {
            return RenderDetailedObject(FilterInternalProperties(metadata.Old.Inputs), (depth + 1) * 4);
        }

        if (metadata.New is not null && metadata.Old is not null)
        {
            var useOutputs = metadata.New.Outputs is { Count: > 0 } &&
                             !string.Equals(resource.Operation, "import", StringComparison.OrdinalIgnoreCase);
            var oldSource = useOutputs ? metadata.Old.Outputs : metadata.Old.Inputs;
            var newSource = useOutputs ? metadata.New.Outputs : metadata.New.Inputs;
            return RenderDetailedDiffObject(FilterInternalProperties(oldSource), FilterInternalProperties(newSource), (depth + 1) * 4);
        }

        return [];
    }

    private static IReadOnlyList<string> BuildResourceSummaryLines(StackSummaryTranscriptRow summary)
    {
        if (summary.Event.ResourceChanges.Count == 0)
        {
            return ["    " + summary.Summary];
        }

        var lines = new List<string>();
        foreach (var change in summary.Event.ResourceChanges.OrderBy(static entry => entry.Key, StringComparer.Ordinal))
        {
            if (change.Value <= 0)
            {
                continue;
            }

            var marker = MarkerForSummaryChange(change.Key);
            var label = string.Equals(change.Key, "same", StringComparison.Ordinal) ? "unchanged" : change.Key;
            lines.Add(string.IsNullOrEmpty(marker)
                ? $"    {change.Value} {label}"
                : $"    {marker} {change.Value} {label}");
        }

        return lines.Count == 0 ? ["    " + summary.Summary] : lines;
    }

    private static string? ExtractDuration(StackSummaryTranscriptRow summary)
    {
        if (summary.Event.DurationSeconds < 0)
        {
            return null;
        }

        return FormatDuration(TimeSpan.FromSeconds(summary.Event.DurationSeconds));
    }

    private static IReadOnlyList<string> BuildSummaryOutputLines(StackResourceTranscriptRow? root)
    {
        if (root is null)
        {
            return [];
        }

        var state = GetStackState(root);
        if (state.newOutputs is not { Count: > 0 })
        {
            return [];
        }

        return RenderDiffObject(state.oldOutputs, state.newOutputs, 2, includeInternalMarkers: true);
    }

    private static (IReadOnlyDictionary<string, JsonElement>? oldOutputs, IReadOnlyDictionary<string, JsonElement>? newOutputs) GetStackState(StackResourceTranscriptRow root)
    {
        return (root.Metadata.Old?.Outputs, root.Metadata.New?.Outputs);
    }

    private static List<string> RenderDiffObject(
        IReadOnlyDictionary<string, JsonElement>? oldValue,
        IReadOnlyDictionary<string, JsonElement>? newValue,
        int indent,
        bool includeInternalMarkers)
    {
        var lines = new List<string>();
        if (newValue is null || newValue.Count == 0)
        {
            return lines;
        }

        var properties = newValue
            .Where(x => includeInternalMarkers || !string.Equals(x.Key, "@isPulumiResource", StringComparison.Ordinal))
            .ToArray();
        var oldProperties = oldValue ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var labelWidth = properties.Length == 0 ? 0 : properties.Max(x => x.Key.Length);

        foreach (var property in properties)
        {
            oldProperties.TryGetValue(property.Key, out var oldProperty);
            var status = DetermineDiffStatus(oldProperty, property.Value);
            var lineIndent = new string(' ', indent);

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var marker = status == ' ' ? "~" : status.ToString();
                lines.Add($"{lineIndent}{marker} {FormatPropertyLabel(property.Key, labelWidth, indent)}: {{");
                lines.AddRange(RenderDiffObject(ToDictionary(oldProperty), ToDictionary(property.Value), indent + 4, includeInternalMarkers));
                lines.Add($"{new string(' ', indent + 2)}}}");
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                lines.Add($"{lineIndent}{(status == ' ' ? " " : status.ToString())} {FormatPropertyLabel(property.Key, labelWidth, indent)}: {FormatJson(property.Value)}");
                continue;
            }

            var prefix = status == ' ' ? "  " : $"{status} ";
            lines.Add($"{lineIndent}{prefix}{FormatPropertyLabel(property.Key, labelWidth, indent)}: {FormatJson(property.Value)}");
        }

        return lines;
    }

    private static List<string> RenderDetailedDiffObject(
        IReadOnlyDictionary<string, JsonElement>? oldValue,
        IReadOnlyDictionary<string, JsonElement>? newValue,
        int indent)
    {
        var lines = new List<string>();
        var keys = GetOrderedKeys(oldValue, newValue);
        if (keys.Count == 0)
        {
            return lines;
        }

        var labelWidth = keys.Max(static key => key.Length);
        foreach (var key in keys)
        {
            JsonElement oldProperty = default;
            JsonElement newProperty = default;
            var hasOld = oldValue is not null && oldValue.TryGetValue(key, out oldProperty);
            var hasNew = newValue is not null && newValue.TryGetValue(key, out newProperty);
            if (!hasOld && !hasNew)
            {
                continue;
            }

            if (hasOld && hasNew && JsonElementEquals(oldProperty, newProperty))
            {
                continue;
            }

            lines.AddRange(RenderDetailedValueDiff(key, hasOld ? oldProperty : default, hasNew ? newProperty : default, hasOld, hasNew, indent, labelWidth));
        }

        return lines;
    }

    private static List<string> RenderDetailedObject(
        IReadOnlyDictionary<string, JsonElement>? value,
        int indent)
    {
        var lines = new List<string>();
        if (value is null || value.Count == 0)
        {
            return lines;
        }

        var properties = GetOrderedEntries(value);
        if (properties.Count == 0)
        {
            return lines;
        }

        var labelWidth = properties.Max(static property => property.Key.Length);
        foreach (var property in properties)
        {
            lines.AddRange(RenderDetailedValue(property.Key, property.Value, indent, labelWidth));
        }

        return lines;
    }

    private static IReadOnlyList<string> RenderDetailedValueDiff(
        string key,
        JsonElement oldValue,
        JsonElement newValue,
        bool hasOld,
        bool hasNew,
        int indent,
        int labelWidth)
    {
        if (!hasOld)
        {
            return RenderDetailedValue(key, newValue, indent, labelWidth, '+');
        }

        if (!hasNew)
        {
            return RenderDetailedValue(key, oldValue, indent, labelWidth, '-');
        }

        if (TryParseStructuredString(oldValue, out var parsedOld, out var oldPrefix) &&
            TryParseStructuredString(newValue, out var parsedNew, out var newPrefix) &&
            string.Equals(oldPrefix, newPrefix, StringComparison.Ordinal))
        {
            return RenderStructuredValueDiff(key, parsedOld, parsedNew, indent, labelWidth, '~', oldPrefix);
        }

        if (IsContainer(oldValue) && IsContainer(newValue) && oldValue.ValueKind == newValue.ValueKind)
        {
            return RenderStructuredValueDiff(key, oldValue, newValue, indent, labelWidth, '~', prefix: null);
        }

        return
        [
            $"{new string(' ', indent)}~ {FormatPropertyLabel(key, labelWidth, indent)}: {FormatJson(oldValue)} => {FormatJson(newValue)}"
        ];
    }

    private static IReadOnlyList<string> RenderDetailedValue(
        string key,
        JsonElement value,
        int indent,
        int labelWidth,
        char? marker = null)
    {
        var lineIndent = new string(' ', indent);
        var prefix = marker is null ? string.Empty : $"{marker} ";

        if (TryParseStructuredString(value, out var parsed, out var stringPrefix))
        {
            return RenderStructuredValue(key, parsed, indent, labelWidth, marker, stringPrefix);
        }

        if (TryFormatSpecialValue(value, out var specialValue))
        {
            return [$"{lineIndent}{prefix}{FormatPropertyLabel(key, labelWidth, indent)}: {specialValue}"];
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            return RenderStructuredValue(key, value, indent, labelWidth, marker, prefix: null);
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return RenderStructuredValue(key, value, indent, labelWidth, marker, prefix: null);
        }

        return [$"{lineIndent}{prefix}{FormatPropertyLabel(key, labelWidth, indent)}: {FormatJson(value)}"];
    }

    private static IReadOnlyList<string> RenderStructuredValue(
        string key,
        JsonElement value,
        int indent,
        int labelWidth,
        char? marker,
        string? prefix)
    {
        var lines = new List<string>();
        var lineIndent = new string(' ', indent);
        var markerPrefix = marker is null ? string.Empty : $"{marker} ";
        var suffix = prefix is null ? string.Empty : $"{prefix} ";

        if (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any())
        {
            lines.Add($"{lineIndent}{markerPrefix}{FormatPropertyLabel(key, labelWidth, indent)}: {suffix}{{}}");
            return lines;
        }

        if (value.ValueKind == JsonValueKind.Array && !value.EnumerateArray().Any())
        {
            lines.Add($"{lineIndent}{markerPrefix}{FormatPropertyLabel(key, labelWidth, indent)}: {suffix}[]");
            return lines;
        }

        var open = value.ValueKind == JsonValueKind.Array ? "[" : "{";
        var close = value.ValueKind == JsonValueKind.Array ? "]" : "}";
        lines.Add($"{lineIndent}{markerPrefix}{FormatPropertyLabel(key, labelWidth, indent)}: {suffix}{open}");
        lines.AddRange(value.ValueKind == JsonValueKind.Array
            ? RenderDetailedArray(value, indent + 4)
            : RenderDetailedObject(ToDictionary(value), indent + 4));
        lines.Add($"{lineIndent}{close}");
        return lines;
    }

    private static IReadOnlyList<string> RenderStructuredValueDiff(
        string key,
        JsonElement oldValue,
        JsonElement newValue,
        int indent,
        int labelWidth,
        char marker,
        string? prefix)
    {
        var lines = new List<string>();
        var lineIndent = new string(' ', indent);
        var suffix = prefix is null ? string.Empty : $"{prefix} ";
        var open = newValue.ValueKind == JsonValueKind.Array ? "[" : "{";
        var close = newValue.ValueKind == JsonValueKind.Array ? "]" : "}";
        lines.Add($"{lineIndent}{marker} {FormatPropertyLabel(key, labelWidth, indent)}: {suffix}{open}");
        lines.AddRange(newValue.ValueKind == JsonValueKind.Array
            ? RenderDetailedArrayDiff(oldValue, newValue, indent + 4)
            : RenderDetailedDiffObject(ToDictionary(oldValue), ToDictionary(newValue), indent + 4));
        lines.Add($"{new string(' ', indent + 2)}{close}");
        return lines;
    }

    private static IReadOnlyList<string> RenderDetailedArray(JsonElement value, int indent)
    {
        var lines = new List<string>();
        var items = value.EnumerateArray().ToArray();
        for (var index = 0; index < items.Length; index++)
        {
            lines.AddRange(RenderArrayItem(items[index], index, indent));
        }

        return lines;
    }

    private static IReadOnlyList<string> RenderDetailedArrayDiff(JsonElement oldValue, JsonElement newValue, int indent)
    {
        var lines = new List<string>();
        var oldItems = oldValue.ValueKind == JsonValueKind.Array ? oldValue.EnumerateArray().ToArray() : [];
        var newItems = newValue.ValueKind == JsonValueKind.Array ? newValue.EnumerateArray().ToArray() : [];
        var count = Math.Max(oldItems.Length, newItems.Length);

        for (var index = 0; index < count; index++)
        {
            var hasOld = index < oldItems.Length;
            var hasNew = index < newItems.Length;
            if (!hasOld && !hasNew)
            {
                continue;
            }

            if (hasOld && hasNew && JsonElementEquals(oldItems[index], newItems[index]))
            {
                continue;
            }

            lines.AddRange(RenderArrayItemDiff(hasOld ? oldItems[index] : default, hasNew ? newItems[index] : default, hasOld, hasNew, index, indent));
        }

        return lines;
    }

    private static IReadOnlyList<string> RenderArrayItem(JsonElement value, int index, int indent, char? marker = null)
    {
        var lineIndent = new string(' ', indent);
        var label = $"[{index}]";
        var prefix = marker is null ? string.Empty : $"{marker} ";

        if (TryParseStructuredString(value, out var parsed, out var stringPrefix))
        {
            return RenderStructuredArrayItem(parsed, index, indent, marker, stringPrefix);
        }

        if (TryFormatSpecialValue(value, out var specialValue))
        {
            return [$"{lineIndent}{prefix}{label}: {specialValue}"];
        }

        if (value.ValueKind == JsonValueKind.Object || value.ValueKind == JsonValueKind.Array)
        {
            return RenderStructuredArrayItem(value, index, indent, marker, prefix: null);
        }

        return [$"{lineIndent}{prefix}{label}: {FormatJson(value)}"];
    }

    private static IReadOnlyList<string> RenderArrayItemDiff(
        JsonElement oldValue,
        JsonElement newValue,
        bool hasOld,
        bool hasNew,
        int index,
        int indent)
    {
        if (!hasOld)
        {
            return RenderArrayItem(newValue, index, indent, '+');
        }

        if (!hasNew)
        {
            return RenderArrayItem(oldValue, index, indent, '-');
        }

        if (TryParseStructuredString(oldValue, out var parsedOld, out var oldPrefix) &&
            TryParseStructuredString(newValue, out var parsedNew, out var newPrefix) &&
            string.Equals(oldPrefix, newPrefix, StringComparison.Ordinal))
        {
            return RenderStructuredArrayItemDiff(parsedOld, parsedNew, index, indent, '~', oldPrefix);
        }

        if (IsContainer(oldValue) && IsContainer(newValue) && oldValue.ValueKind == newValue.ValueKind)
        {
            return RenderStructuredArrayItemDiff(oldValue, newValue, index, indent, '~', prefix: null);
        }

        return [$"{new string(' ', indent)}~ [{index}]: {FormatJson(oldValue)} => {FormatJson(newValue)}"];
    }

    private static IReadOnlyList<string> RenderStructuredArrayItem(
        JsonElement value,
        int index,
        int indent,
        char? marker,
        string? prefix)
    {
        var lines = new List<string>();
        var lineIndent = new string(' ', indent);
        var markerPrefix = marker is null ? string.Empty : $"{marker} ";
        var suffix = prefix is null ? string.Empty : $"{prefix} ";

        if (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any())
        {
            lines.Add($"{lineIndent}{markerPrefix}[{index}]: {suffix}{{}}");
            return lines;
        }

        if (value.ValueKind == JsonValueKind.Array && !value.EnumerateArray().Any())
        {
            lines.Add($"{lineIndent}{markerPrefix}[{index}]: {suffix}[]");
            return lines;
        }

        var open = value.ValueKind == JsonValueKind.Array ? "[" : "{";
        var close = value.ValueKind == JsonValueKind.Array ? "]" : "}";
        lines.Add($"{lineIndent}{markerPrefix}[{index}]: {suffix}{open}");
        lines.AddRange(value.ValueKind == JsonValueKind.Array
            ? RenderDetailedArray(value, indent + 4)
            : RenderDetailedObject(ToDictionary(value), indent + 4));
        lines.Add($"{lineIndent}{close}");
        return lines;
    }

    private static IReadOnlyList<string> RenderStructuredArrayItemDiff(
        JsonElement oldValue,
        JsonElement newValue,
        int index,
        int indent,
        char marker,
        string? prefix)
    {
        var lines = new List<string>();
        var lineIndent = new string(' ', indent);
        var suffix = prefix is null ? string.Empty : $"{prefix} ";
        var open = newValue.ValueKind == JsonValueKind.Array ? "[" : "{";
        var close = newValue.ValueKind == JsonValueKind.Array ? "]" : "}";
        lines.Add($"{lineIndent}{marker} [{index}]: {suffix}{open}");
        lines.AddRange(newValue.ValueKind == JsonValueKind.Array
            ? RenderDetailedArrayDiff(oldValue, newValue, indent + 4)
            : RenderDetailedDiffObject(ToDictionary(oldValue), ToDictionary(newValue), indent + 4));
        lines.Add($"{new string(' ', indent + 2)}{close}");
        return lines;
    }

    private static IReadOnlyList<KeyValuePair<string, JsonElement>> GetOrderedEntries(IReadOnlyDictionary<string, JsonElement> properties)
    {
        return properties
            .Where(static entry => !string.Equals(entry.Key, "@isPulumiResource", StringComparison.Ordinal))
            .OrderBy(static entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> GetOrderedKeys(
        IReadOnlyDictionary<string, JsonElement>? oldValue,
        IReadOnlyDictionary<string, JsonElement>? newValue)
    {
        return (oldValue?.Keys ?? Array.Empty<string>())
            .Concat(newValue?.Keys ?? Array.Empty<string>())
            .Where(static key => !string.Equals(key, "@isPulumiResource", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool TryParseStructuredString(JsonElement value, out JsonElement parsed, out string? prefix)
    {
        parsed = default;
        prefix = null;

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (!(trimmed.StartsWith('{') || trimmed.StartsWith('[')))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            parsed = document.RootElement.Clone();
            prefix = "(json)";
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsContainer(JsonElement value)
        => value.ValueKind is JsonValueKind.Object or JsonValueKind.Array;

    private static IReadOnlyList<string> RenderObjectForDiff(IReadOnlyDictionary<string, JsonElement>? value, int indent)
    {
        var lines = new List<string>();
        if (value is null || value.Count == 0)
        {
            return lines;
        }

        var properties = FilterInternalProperties(value)?.ToArray() ?? [];
        var labelWidth = properties.Length == 0 ? 0 : properties.Max(x => x.Key.Length);
        foreach (var property in properties)
        {
            var lineIndent = new string(' ', indent);
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                lines.Add($"{lineIndent}{property.Key}: {{");
                lines.AddRange(RenderObjectForDiff(ToDictionary(property.Value), indent + 4));
                lines.Add($"{lineIndent}}}");
            }
            else
            {
                lines.Add($"{lineIndent}{FormatPropertyLabel(property.Key, labelWidth, indent)}: {FormatJson(property.Value)}");
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

    private static IReadOnlyDictionary<string, JsonElement>? ToDictionary(JsonElement value)
    {
        return value.ValueKind != JsonValueKind.Object
            ? null
            : value.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value, StringComparer.Ordinal);
    }

    private static string FormatJson(JsonElement value)
    {
        if (TryFormatSpecialValue(value, out var specialValue))
        {
            return specialValue;
        }

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
            ? $"  {resource.ResourceType}: ({op})"
            : $"{resource.Marker} {resource.ResourceType}: ({op})";
    }

    private static string MarkerForSummaryChange(string change)
    {
        return change switch
        {
            "create" => "+",
            "delete" => "-",
            "import" => "<",
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

    private static void AppendSummaryColumn(StringBuilder builder, string value, int width)
    {
        if (value.Length >= width)
        {
            builder.Append(value);
            builder.Append("  ");
            return;
        }

        builder.Append(value.PadRight(width));
    }

    private static IReadOnlyDictionary<string, JsonElement>? FilterInternalProperties(IReadOnlyDictionary<string, JsonElement>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return properties;
        }

        var filtered = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in properties)
        {
            if (entry.Key.StartsWith("__", StringComparison.Ordinal) ||
                string.Equals(entry.Key, "@isPulumiResource", StringComparison.Ordinal))
            {
                continue;
            }

            filtered[entry.Key] = entry.Value;
        }

        return filtered;
    }

    private static bool TryFormatSpecialValue(JsonElement value, out string formatted)
    {
        formatted = string.Empty;

        if (value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), UnknownSignatureValue, StringComparison.Ordinal))
        {
            formatted = "[unknown]";
            return true;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (value.TryGetProperty(PulumiSignatureKey, out var signature))
        {
            var signatureValue = signature.GetString();
            if (string.Equals(signatureValue, SecretSignatureValue, StringComparison.Ordinal))
            {
                formatted = "[secret]";
                return true;
            }

            if (string.Equals(signatureValue, UnknownSignatureValue, StringComparison.Ordinal))
            {
                formatted = "[unknown]";
                return true;
            }
        }

        return false;
    }

    private static bool IsDefaultProvider(string provider)
    {
        var separator = provider.LastIndexOf("::", StringComparison.Ordinal);
        if (separator < 0 || separator == provider.Length - 2)
        {
            return false;
        }

        var name = provider[(separator + 2)..];
        return string.Equals(name, "default", StringComparison.Ordinal);
    }

    private static bool ShouldUseCreateOutputs(StepEventStateMetadata newState)
        => newState.Outputs is { Count: > 0 } &&
           !string.IsNullOrWhiteSpace(newState.Id);
}
