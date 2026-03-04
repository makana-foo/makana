using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Makana.WebConsole.Models;

namespace Makana.WebConsole.Services;

public sealed partial class StackUpdateTranscriptMapper
{
    public IReadOnlyList<StackTranscriptRow> Map(IReadOnlyList<JsonElement> events)
    {
        if (events.Count == 0)
        {
            return [];
        }

        var rows = new List<StackTranscriptRow>(events.Count);
        var resourceRowsByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < events.Count; index++)
        {
            var candidate = UnwrapEvent(events[index]);
            if (candidate is null)
            {
                continue;
            }

            if (TryMapConfiguration(candidate.Value, index, out var configurationRow))
            {
                rows.Add(configurationRow);
                continue;
            }

            if (TryMapResourcePre(candidate.Value, index, rows, resourceRowsByIdentity))
            {
                continue;
            }

            if (TryUpdateResourceOutputs(candidate.Value, index, rows, resourceRowsByIdentity))
            {
                continue;
            }

            if (TryUpdateResourceFailure(candidate.Value, index, rows, resourceRowsByIdentity))
            {
                continue;
            }

            if (TryMapDiagnostic(candidate.Value, index, out var diagnosticRow))
            {
                rows.Add(diagnosticRow);
                continue;
            }

            if (TryMapStdout(candidate.Value, index, out var textRow))
            {
                rows.Add(textRow);
                continue;
            }

            if (TryMapSummary(candidate.Value, index, out var summaryRow))
            {
                rows.Add(summaryRow);
                continue;
            }

            if (TryMapFallbackEngineEvent(candidate.Value, index, out var fallbackRow))
            {
                rows.Add(fallbackRow);
            }
        }

        return rows;
    }

    private static JsonElement? UnwrapEvent(JsonElement ev)
    {
        if (IsJournalEntry(ev))
        {
            return null;
        }

        return ev;
    }

    private static bool TryMapConfiguration(JsonElement ev, int index, out StackConfigurationTranscriptRow row)
    {
        row = default!;

        if (!TryGetPropertyObject(ev, "preludeEvent", out var preludeEvent) ||
            !TryGetPropertyObject(preludeEvent, "config", out var config))
        {
            return false;
        }

        var entries = new List<KeyValuePair<string, string>>();
        foreach (var property in config.EnumerateObject())
        {
            entries.Add(new KeyValuePair<string, string>(property.Name, JsonValueToInlineString(property.Value)));
        }

        row = new StackConfigurationTranscriptRow(
            EventKey(index),
            entries,
            JsonSerializer.Serialize(ev, JsonWriteIndented));
        return true;
    }

    private static bool TryMapResourcePre(
        JsonElement ev,
        int index,
        List<StackTranscriptRow> rows,
        Dictionary<string, int> resourceRowsByIdentity)
    {
        if (!TryGetPropertyObject(ev, "resourcePreEvent", out var preEvent) ||
            !TryGetPropertyObject(preEvent, "metadata", out var metadata))
        {
            return false;
        }

        var operation = GetStringOrDefault(metadata, "op", "update");
        var urn = GetOptionalString(metadata, "urn");
        var parentUrn = GetNestedOptionalString(metadata, "new", "parent") ?? GetNestedOptionalString(metadata, "old", "parent");
        var resourceType = GetOptionalString(metadata, "type") ?? TryExtractResourceType(urn);
        var resourceName = TryExtractResourceName(urn);
        var key = EventKey(index);
        var raw = JsonSerializer.Serialize(ev, JsonWriteIndented);

        var row = new StackResourceTranscriptRow(
            key,
            MarkerForOperation(operation),
            operation,
            urn,
            parentUrn,
            resourceType,
            resourceName,
            PresentTenseForOperation(operation),
            string.Empty,
            raw,
            IsFailureOperation(operation));

        resourceRowsByIdentity[ResourceIdentity(operation, urn)] = rows.Count;
        rows.Add(row);
        return true;
    }

    private static bool TryUpdateResourceOutputs(
        JsonElement ev,
        int index,
        List<StackTranscriptRow> rows,
        Dictionary<string, int> resourceRowsByIdentity)
    {
        if (!TryGetPropertyObject(ev, "resOutputsEvent", out var outputsEvent) ||
            !TryGetPropertyObject(outputsEvent, "metadata", out var metadata))
        {
            return false;
        }

        var operation = GetStringOrDefault(metadata, "op", "same");
        var urn = GetOptionalString(metadata, "urn");
        var identity = ResourceIdentity(operation, urn);
        var raw = JsonSerializer.Serialize(ev, JsonWriteIndented);

        var resourceType = GetOptionalString(metadata, "type") ?? TryExtractResourceType(urn);
        var parentUrn = GetNestedOptionalString(metadata, "new", "parent") ?? GetNestedOptionalString(metadata, "old", "parent");
        var resourceName = TryExtractResourceName(urn);
        var status = PastTenseForOperation(operation);
        var info = FormatResourceInfo(metadata);
        var marker = MarkerForOperation(operation);

        if (resourceRowsByIdentity.TryGetValue(identity, out var rowIndex) &&
            rows[rowIndex] is StackResourceTranscriptRow existing)
        {
            rows[rowIndex] = existing with
            {
                Marker = marker,
                Operation = operation,
                ResourceUrn = urn,
                ParentUrn = parentUrn,
                ResourceType = resourceType,
                ResourceName = resourceName,
                Status = status,
                Info = info,
                RawPayloadJson = raw,
                IsError = existing.IsError || IsFailureOperation(operation)
            };
            return true;
        }

        rows.Add(new StackResourceTranscriptRow(
            EventKey(index),
            marker,
            operation,
            urn,
            parentUrn,
            resourceType,
            resourceName,
            status,
            info,
            raw,
            IsFailureOperation(operation)));
        return true;
    }

    private static bool TryUpdateResourceFailure(
        JsonElement ev,
        int index,
        List<StackTranscriptRow> rows,
        Dictionary<string, int> resourceRowsByIdentity)
    {
        if (!TryGetPropertyObject(ev, "resOpFailedEvent", out var failedEvent) ||
            !TryGetPropertyObject(failedEvent, "metadata", out var metadata))
        {
            return false;
        }

        var operation = GetStringOrDefault(metadata, "op", "update");
        var urn = GetOptionalString(metadata, "urn");
        var identity = ResourceIdentity(operation, urn);
        var raw = JsonSerializer.Serialize(ev, JsonWriteIndented);
        var resourceType = GetOptionalString(metadata, "type") ?? TryExtractResourceType(urn);
        var parentUrn = GetNestedOptionalString(metadata, "new", "parent") ?? GetNestedOptionalString(metadata, "old", "parent");
        var resourceName = TryExtractResourceName(urn);
        var status = $"{PresentTenseForOperation(operation)} failed";
        var info = FormatResourceInfo(metadata);
        var marker = MarkerForOperation(operation);

        if (resourceRowsByIdentity.TryGetValue(identity, out var rowIndex) &&
            rows[rowIndex] is StackResourceTranscriptRow existing)
        {
            rows[rowIndex] = existing with
            {
                Marker = marker,
                Operation = operation,
                ResourceUrn = urn,
                ParentUrn = parentUrn,
                ResourceType = resourceType,
                ResourceName = resourceName,
                Status = status,
                Info = info,
                RawPayloadJson = raw,
                IsError = true
            };
            return true;
        }

        rows.Add(new StackResourceTranscriptRow(
            EventKey(index),
            marker,
            operation,
            urn,
            parentUrn,
            resourceType,
            resourceName,
            status,
            info,
            raw,
            true));
        return true;
    }

    private static bool TryMapDiagnostic(JsonElement ev, int index, out StackDiagnosticTranscriptRow row)
    {
        row = default!;

        if (!TryGetPropertyObject(ev, "diagnosticEvent", out var diagnosticEvent))
        {
            return false;
        }

        var severity = NormalizeSeverity(GetStringOrDefault(diagnosticEvent, "severity", "info"));
        var prefix = NormalizeConsoleText(GetOptionalString(diagnosticEvent, "prefix"));
        var message = NormalizeConsoleText(GetStringOrDefault(diagnosticEvent, "message", string.Empty));
        var urn = GetOptionalString(diagnosticEvent, "urn");
        var isError = string.Equals(severity, "error", StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            message = $"{prefix}{message}";
        }

        row = new StackDiagnosticTranscriptRow(
            EventKey(index),
            severity,
            urn,
            message,
            JsonSerializer.Serialize(ev, JsonWriteIndented),
            isError);
        return true;
    }

    private static bool TryMapStdout(JsonElement ev, int index, out StackTextTranscriptRow row)
    {
        row = default!;

        if (!TryGetPropertyObject(ev, "stdoutEvent", out var stdoutEvent))
        {
            return false;
        }

        var message = NormalizeConsoleText(GetStringOrDefault(stdoutEvent, "message", string.Empty));
        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        row = new StackTextTranscriptRow(
            EventKey(index),
            "info",
            message,
            JsonSerializer.Serialize(ev, JsonWriteIndented),
            false);
        return true;
    }

    private static bool TryMapSummary(JsonElement ev, int index, out StackSummaryTranscriptRow row)
    {
        row = default!;

        if (!TryGetPropertyObject(ev, "summaryEvent", out var summaryEvent))
        {
            return false;
        }

        var summary = new StringBuilder();
        if (summaryEvent.TryGetProperty("resourceChanges", out var resourceChanges) &&
            resourceChanges.ValueKind == JsonValueKind.Object)
        {
            var changes = new List<string>();
            foreach (var entry in resourceChanges.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var count) && count > 0)
                {
                    changes.Add($"{count} {entry.Name}");
                }
            }

            if (changes.Count > 0)
            {
                summary.Append(string.Join(", ", changes));
            }
        }

        if (TryGetDouble(summaryEvent, "durationSeconds", out var durationSeconds))
        {
            if (summary.Length > 0)
            {
                summary.Append(" · ");
            }

            summary.Append($"duration {durationSeconds:0.###}s");
        }

        if (TryGetBoolean(summaryEvent, "maybeCorrupt", out var maybeCorrupt) && maybeCorrupt)
        {
            if (summary.Length > 0)
            {
                summary.Append(" · ");
            }

            summary.Append("checkpoint may be corrupt");
        }

        if (summary.Length == 0)
        {
            summary.Append("summary");
        }

        row = new StackSummaryTranscriptRow(
            EventKey(index),
            summary.ToString(),
            JsonSerializer.Serialize(ev, JsonWriteIndented));
        return true;
    }

    private static bool TryMapFallbackEngineEvent(JsonElement ev, int index, out StackUnknownTranscriptRow row)
    {
        row = default!;

        if (!LooksLikeEngineEventEnvelope(ev))
        {
            return false;
        }

        row = new StackUnknownTranscriptRow(
            EventKey(index),
            NormalizeConsoleText(ev.GetRawText()),
            JsonSerializer.Serialize(ev, JsonWriteIndented));
        return true;
    }

    private static string ResourceIdentity(string operation, string? urn)
        => $"{operation}::{urn}";

    private static string EventKey(int index)
        => $"event-{index + 1:D3}";

    private static string? FormatResourceInfo(JsonElement metadata)
    {
        var fragments = new List<string>();

        if (TryGetInt64(metadata, "duration", out var durationMs))
        {
            fragments.Add(FormatDuration(durationMs));
        }

        if (metadata.TryGetProperty("detailedDiff", out var detailedDiff) &&
            detailedDiff.ValueKind == JsonValueKind.Object)
        {
            var count = detailedDiff.EnumerateObject().Count();
            if (count > 0)
            {
                fragments.Add($"diff {count}");
            }
        }

        return fragments.Count == 0 ? null : string.Join(" · ", fragments);
    }

    private static string PresentTenseForOperation(string operation)
    {
        return operation.ToLowerInvariant() switch
        {
            "create" => "creating",
            "create-replacement" => "creating replacement",
            "delete" => "deleting",
            "delete-replaced" => "deleting replaced",
            "replace" => "replacing",
            "read" => "reading",
            "read-replacement" => "reading replacement",
            "same" => "same",
            "update" => "updating",
            "import" => "importing",
            _ => operation.ToLowerInvariant()
        };
    }

    private static string PastTenseForOperation(string operation)
    {
        return operation.ToLowerInvariant() switch
        {
            "create" => "created",
            "create-replacement" => "created replacement",
            "delete" => "deleted",
            "delete-replaced" => "deleted replaced",
            "replace" => "replaced",
            "read" => "read",
            "read-replacement" => "read replacement",
            "same" => "same",
            "update" => "updated",
            "import" => "imported",
            _ => operation.ToLowerInvariant()
        };
    }

    private static string MarkerForOperation(string operation)
    {
        return operation.ToLowerInvariant() switch
        {
            "create" => "+",
            "create-replacement" => "++",
            "delete" => "-",
            "delete-replaced" => "--",
            "replace" => "+-",
            "read" => ">",
            "read-replacement" => ">>",
            "same" => "=",
            "update" => "~",
            "import" => "<",
            _ => "·"
        };
    }

    private static string NormalizeSeverity(string severity)
        => severity.Replace("#err", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().ToLowerInvariant();

    private static string NormalizeConsoleText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var withoutPulumiMarkup = PulumiMarkupRegex().Replace(value, string.Empty);
        var withoutAnsi = AnsiEscapeRegex().Replace(withoutPulumiMarkup, string.Empty);
        return withoutAnsi
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .TrimEnd();
    }

    private static bool IsJournalEntry(JsonElement ev)
    {
        return ev.ValueKind == JsonValueKind.Object &&
               ev.TryGetProperty("kind", out var kind) &&
               (kind.ValueKind == JsonValueKind.Number || kind.ValueKind == JsonValueKind.String) &&
               ev.TryGetProperty("sequenceID", out _);
    }

    private static bool LooksLikeEngineEventEnvelope(JsonElement ev)
    {
        return ev.ValueKind == JsonValueKind.Object &&
               (ev.TryGetProperty("sequence", out _) || ev.TryGetProperty("timestamp", out _));
    }

    private static bool IsFailureOperation(string operation)
        => operation.Contains("fail", StringComparison.OrdinalIgnoreCase);

    private static string? TryExtractResourceType(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return null;
        }

        var index = urn.LastIndexOf("::", StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var beforeName = urn[..index];
        var typeIndex = beforeName.LastIndexOf("::", StringComparison.Ordinal);
        return typeIndex < 0 ? null : beforeName[(typeIndex + 2)..];
    }

    private static string? TryExtractResourceName(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            return null;
        }

        var index = urn.LastIndexOf("::", StringComparison.Ordinal);
        return index < 0 ? urn : urn[(index + 2)..];
    }

    private static string FormatDuration(long durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        return duration.TotalSeconds >= 1
            ? $"{duration.TotalSeconds:0.##}s"
            : $"{duration.TotalMilliseconds:0}ms";
    }

    private static string JsonValueToInlineString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null => "null",
            _ => NormalizeConsoleText(value.GetRawText())
        };
    }

    private static bool TryGetPropertyObject(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string GetStringOrDefault(JsonElement element, string propertyName, string defaultValue)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? defaultValue
            : defaultValue;
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetNestedOptionalString(JsonElement element, string objectPropertyName, string propertyName)
    {
        if (!TryGetPropertyObject(element, objectPropertyName, out var nested))
        {
            return null;
        }

        return GetOptionalString(nested, propertyName);
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               (value = property.GetBoolean()) == value;
    }

    private static bool TryGetInt64(JsonElement element, string propertyName, out long value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetInt64(out value);
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = default;
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value);
    }

    private static readonly JsonSerializerOptions JsonWriteIndented = new() { WriteIndented = true };

    [GeneratedRegex(@"<\{%.+?%\}>", RegexOptions.Compiled)]
    private static partial Regex PulumiMarkupRegex();

    [GeneratedRegex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled)]
    private static partial Regex AnsiEscapeRegex();
}
