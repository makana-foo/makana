using System.Text.Json;
using Makana.StackUpdates.Pulumi;

namespace Makana.Api.Updates;

internal sealed class ImportUpdateEventFactory
{
    public IReadOnlyList<EngineEvent> CreateEvents(JsonElement deployment)
    {
        if (!deployment.TryGetProperty("resources", out var resourcesElement) ||
            resourcesElement.ValueKind != JsonValueKind.Array)
        {
            return [BuildSummaryEvent(0, 1)];
        }

        var resources = resourcesElement.EnumerateArray()
            .Select(BuildResourceState)
            .Where(state => state is not null)
            .Cast<StepEventStateMetadata>()
            .Where(state => !IsProviderResource(state))
            .ToArray();

        var events = new List<EngineEvent>(resources.Length * 2 + 1);
        var sequence = 1;
        foreach (var resource in resources)
        {
            var metadata = BuildMetadata(resource);
            events.Add(new EngineEvent(
                sequence++,
                CurrentTimestamp(),
                ResourcePreEvent: new ResourcePreEvent(metadata, null)));
            events.Add(new EngineEvent(
                sequence++,
                CurrentTimestamp(),
                ResOutputsEvent: new ResOutputsEvent(metadata, null)));
        }

        events.Add(BuildSummaryEvent(resources.Length, sequence));
        return events;
    }

    private static EngineEvent BuildSummaryEvent(int importCount, int sequence)
    {
        return new EngineEvent(
            sequence,
            CurrentTimestamp(),
            SummaryEvent: new SummaryEvent(
                false,
                0,
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["import"] = importCount
                },
                new Dictionary<string, string>(StringComparer.Ordinal),
                false));
    }

    private static StepEventMetadata BuildMetadata(StepEventStateMetadata resourceState)
    {
        return new StepEventMetadata(
            "import",
            resourceState.Urn,
            resourceState.Type,
            BuildOldState(resourceState),
            resourceState,
            null,
            null,
            null,
            true,
            resourceState.Provider);
    }

    private static StepEventStateMetadata BuildOldState(StepEventStateMetadata resourceState)
    {
        return new StepEventStateMetadata(
            resourceState.Type,
            resourceState.Urn,
            resourceState.Custom,
            resourceState.Delete,
            resourceState.Id,
            resourceState.Parent,
            resourceState.Protect,
            resourceState.Taint,
            resourceState.RetainOnDelete,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            resourceState.Provider,
            resourceState.InitErrors,
            resourceState.HideDiffs);
    }

    private static StepEventStateMetadata? BuildResourceState(JsonElement resource)
    {
        if (!resource.TryGetProperty("urn", out var urnElement) ||
            urnElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(urnElement.GetString()) ||
            !resource.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(typeElement.GetString()))
        {
            return null;
        }

        return new StepEventStateMetadata(
            typeElement.GetString(),
            urnElement.GetString(),
            GetOptionalBoolean(resource, "custom"),
            GetOptionalBoolean(resource, "delete"),
            GetString(resource, "id"),
            GetString(resource, "parent"),
            GetOptionalBoolean(resource, "protect"),
            GetOptionalBoolean(resource, "taint"),
            GetOptionalBoolean(resource, "retainOnDelete"),
            CloneObject(resource, "inputs"),
            CloneObject(resource, "outputs"),
            GetString(resource, "provider"),
            null,
            null);
    }

    private static bool IsProviderResource(StepEventStateMetadata resourceState)
        => resourceState.Type?.StartsWith("pulumi:providers:", StringComparison.Ordinal) == true;

    private static int CurrentTimestamp()
        => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool? GetOptionalBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
    }

    private static Dictionary<string, JsonElement>? CloneObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        return property.EnumerateObject()
            .ToDictionary(static item => item.Name, static item => item.Value.Clone(), StringComparer.Ordinal);
    }
}
