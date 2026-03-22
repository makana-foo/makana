using System.Text.Json;

namespace Makana.Journaling.Checkpointing;

public enum CheckpointPropertyKind
{
    Null = 0,
    String = 1,
    Number = 2,
    Boolean = 3,
    Object = 4,
    Array = 5,
}

public sealed record CheckpointPropertyValue(
    CheckpointPropertyKind Kind,
    string? StringValue = null,
    double? NumberValue = null,
    bool? BooleanValue = null,
    IReadOnlyDictionary<string, CheckpointPropertyValue>? ObjectValue = null,
    IReadOnlyList<CheckpointPropertyValue>? ArrayValue = null)
{
    public static CheckpointPropertyValue Null() => new(CheckpointPropertyKind.Null);

    public static CheckpointPropertyValue String(string value) => new(CheckpointPropertyKind.String, StringValue: value);

    public static CheckpointPropertyValue Number(double value) => new(CheckpointPropertyKind.Number, NumberValue: value);

    public static CheckpointPropertyValue Boolean(bool value) => new(CheckpointPropertyKind.Boolean, BooleanValue: value);

    public static CheckpointPropertyValue Object(IReadOnlyDictionary<string, CheckpointPropertyValue> value)
        => new(CheckpointPropertyKind.Object, ObjectValue: value);

    public static CheckpointPropertyValue Array(IReadOnlyList<CheckpointPropertyValue> value)
        => new(CheckpointPropertyKind.Array, ArrayValue: value);

    public double AsNumber()
    {
        if (Kind != CheckpointPropertyKind.Number || NumberValue is null)
        {
            throw new InvalidOperationException("Checkpoint property is not a number.");
        }

        return NumberValue.Value;
    }
}

public sealed record CheckpointResource(
    string Urn,
    string? Type = null,
    bool Custom = false,
    string? Id = null,
    string? Parent = null,
    bool RefreshBeforeUpdate = false,
    string? ViewOf = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ResourceHooks = null,
    bool Taint = false,
    IReadOnlyList<string>? ReplaceWith = null,
    IReadOnlyDictionary<string, CheckpointPropertyValue>? Inputs = null,
    IReadOnlyDictionary<string, CheckpointPropertyValue>? Outputs = null);

public sealed record CheckpointSnapshot(
    IReadOnlyList<CheckpointResource> Resources,
    string? Stack = null,
    string? SecretsProviderType = null,
    IReadOnlyDictionary<string, CheckpointPropertyValue>? SecretsProviderState = null);

public sealed record VersionedCheckpointDocument(
    int Version,
    IReadOnlyList<string> Features,
    CheckpointSnapshot Latest);

public sealed class UnsupportedCheckpointFeaturesException(IReadOnlyList<string> features)
    : InvalidOperationException($"unsupported checkpoint features: {string.Join(", ", features)}")
{
    public IReadOnlyList<string> Features { get; } = features;
}

public static class CheckpointSerializer
{
    public static VersionedCheckpointDocument DeserializeVersionedCheckpoint(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.TryGetProperty("checkpoint", out var checkpointElement))
        {
            var rawVersion = root.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.Number
                ? versionElement.GetInt32()
                : 3;

            var features = root.TryGetProperty("features", out var featuresElement) && featuresElement.ValueKind == JsonValueKind.Array
                ? featuresElement.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray()
                : [];

            var version = rawVersion < 3 ? 3 : rawVersion;
            CheckpointSchemaRegistry.GetCodec(version).ValidateEnvelope(version, features);

            var stackName = checkpointElement.TryGetProperty("stack", out var stackElement) && stackElement.ValueKind == JsonValueKind.String
                ? stackElement.GetString()
                : null;

            var latestElement = checkpointElement.GetProperty("latest");
            return new VersionedCheckpointDocument(
                Version: version,
                Features: features,
                Latest: DeserializeSnapshot(latestElement, stackName));
        }

        var legacySnapshot = root.TryGetProperty("latest", out var legacyLatest) ? legacyLatest : root;
        var legacyStack = root.TryGetProperty("target", out var legacyTarget) && legacyTarget.ValueKind == JsonValueKind.String
            ? legacyTarget.GetString()
            : null;

        return new VersionedCheckpointDocument(
            Version: DeploymentSchemaVersions.Current,
            Features: [],
            Latest: DeserializeSnapshot(legacySnapshot, legacyStack));
    }

    public static VersionedCheckpointDocument SerializeCheckpoint(string stackName, CheckpointSnapshot snapshot, bool showSecrets)
    {
        var features = DeploymentSchemaFeatures.DetectCheckpointFeatures(snapshot.Resources);
        var latest = snapshot with
        {
            Stack = stackName,
            Resources = snapshot.Resources.Select(CloneResource).ToArray(),
            SecretsProviderState = snapshot.SecretsProviderState is null
                ? null
                : new Dictionary<string, CheckpointPropertyValue>(snapshot.SecretsProviderState, StringComparer.Ordinal),
        };

        return CheckpointSchemaRegistry.ResolveForFeatures(features).CreateEnvelope(latest, features);
    }

    public static string SerializeVersionedCheckpointDocument(VersionedCheckpointDocument document, bool showSecrets)
        => JsonSerializer.Serialize(ToWireDocument(document, showSecrets), CheckpointWireValueConverter.JsonOptions);

    private static object ToWireDocument(VersionedCheckpointDocument document, bool showSecrets)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["version"] = document.Version,
            ["features"] = document.Features.ToArray(),
            ["latest"] = ToWireSnapshot(document.Latest, showSecrets),
        };

    private static object ToWireSnapshot(CheckpointSnapshot snapshot, bool showSecrets)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["stack"] = snapshot.Stack ?? string.Empty,
            ["resources"] = snapshot.Resources.Select(resource => ToWireResource(resource, showSecrets)).ToArray(),
        };

        if (snapshot.SecretsProviderType is not null || snapshot.SecretsProviderState is not null)
        {
            root["secrets_providers"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = snapshot.SecretsProviderType ?? string.Empty,
                ["state"] = snapshot.SecretsProviderState is null
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    : snapshot.SecretsProviderState.ToDictionary(
                        static pair => pair.Key,
                        pair => CheckpointWireValueConverter.ToWireValue(pair.Value, showSecrets),
                        StringComparer.Ordinal),
            };
        }

        return root;
    }

    private static object ToWireResource(CheckpointResource resource, bool showSecrets)
    {
        var wire = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["urn"] = resource.Urn,
            ["custom"] = resource.Custom,
        };

        AddIfNotNull(wire, "type", resource.Type);
        AddIfNotNull(wire, "id", resource.Id);
        AddIfNotNull(wire, "parent", resource.Parent);
        AddIfNotNull(wire, "viewOf", resource.ViewOf);
        AddIfNotNull(wire, "replaceWith", resource.ReplaceWith?.ToArray());

        if (resource.RefreshBeforeUpdate)
        {
            wire["refreshBeforeUpdate"] = true;
        }

        if (resource.Taint)
        {
            wire["taint"] = true;
        }

        if (resource.ResourceHooks is not null)
        {
            wire["resourceHooks"] = resource.ResourceHooks.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Cast<object?>().ToArray(),
                StringComparer.Ordinal);
        }

        if (resource.Inputs is not null)
        {
            wire["inputs"] = resource.Inputs.ToDictionary(
                static pair => pair.Key,
                pair => CheckpointWireValueConverter.ToWireValue(pair.Value, showSecrets),
                StringComparer.Ordinal);
        }

        if (resource.Outputs is not null)
        {
            wire["outputs"] = resource.Outputs.ToDictionary(
                static pair => pair.Key,
                pair => CheckpointWireValueConverter.ToWireValue(pair.Value, showSecrets),
                StringComparer.Ordinal);
        }

        return wire;
    }

    private static CheckpointSnapshot DeserializeSnapshot(JsonElement snapshotElement, string? stackName)
    {
        var resources = snapshotElement.TryGetProperty("resources", out var resourcesElement) && resourcesElement.ValueKind == JsonValueKind.Array
            ? resourcesElement.EnumerateArray().Select(DeserializeResource).ToArray()
            : [];

        string? secretsProviderType = null;
        IReadOnlyDictionary<string, CheckpointPropertyValue>? secretsProviderState = null;

        if (snapshotElement.TryGetProperty("secrets_providers", out var providerElement) && providerElement.ValueKind == JsonValueKind.Object)
        {
            secretsProviderType = providerElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;

            if (providerElement.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.Object)
            {
                secretsProviderState = stateElement.EnumerateObject().ToDictionary(
                    static property => property.Name,
                    static property => CheckpointWireValueConverter.FromWireValue(property.Value, secretsManagerAvailable: true),
                    StringComparer.Ordinal);
            }
        }

        return new CheckpointSnapshot(
            Resources: resources,
            Stack: stackName,
            SecretsProviderType: secretsProviderType,
            SecretsProviderState: secretsProviderState);
    }

    private static CheckpointResource DeserializeResource(JsonElement resourceElement)
        => new(
            Urn: RequireString(resourceElement, "urn"),
            Type: TryGetString(resourceElement, "type"),
            Custom: TryGetBoolean(resourceElement, "custom"),
            Id: TryGetString(resourceElement, "id"),
            Parent: TryGetString(resourceElement, "parent"),
            RefreshBeforeUpdate: TryGetBoolean(resourceElement, "refreshBeforeUpdate"),
            ViewOf: TryGetString(resourceElement, "viewOf"),
            ResourceHooks: TryGetStringListMap(resourceElement, "resourceHooks"),
            Taint: TryGetBoolean(resourceElement, "taint"),
            ReplaceWith: TryGetStringList(resourceElement, "replaceWith"),
            Inputs: TryGetProperties(resourceElement, "inputs"),
            Outputs: TryGetProperties(resourceElement, "outputs"));

    private static CheckpointResource CloneResource(CheckpointResource resource)
        => resource with
        {
            Inputs = resource.Inputs is null
                ? null
                : new Dictionary<string, CheckpointPropertyValue>(resource.Inputs, StringComparer.Ordinal),
            Outputs = resource.Outputs is null
                ? null
                : new Dictionary<string, CheckpointPropertyValue>(resource.Outputs, StringComparer.Ordinal),
            ResourceHooks = resource.ResourceHooks is null
                ? null
                : new Dictionary<string, IReadOnlyList<string>>(resource.ResourceHooks, StringComparer.Ordinal),
            ReplaceWith = resource.ReplaceWith?.ToArray(),
        };

    private static IReadOnlyDictionary<string, CheckpointPropertyValue>? TryGetProperties(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return propertyElement.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => CheckpointWireValueConverter.FromWireValue(property.Value, secretsManagerAvailable: true),
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? TryGetStringListMap(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return propertyElement.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => (IReadOnlyList<string>)property.Value.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray(),
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<string>? TryGetStringList(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return propertyElement.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString() ?? string.Empty)
            .ToArray();
    }

    private static string RequireString(JsonElement element, string propertyName)
        => TryGetString(element, propertyName) ?? throw new InvalidOperationException($"missing required property '{propertyName}'");

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : null;

    private static bool TryGetBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? propertyElement.GetBoolean()
            : false;

    private static void AddIfNotNull(Dictionary<string, object?> dictionary, string key, object? value)
    {
        if (value is not null)
        {
            dictionary[key] = value;
        }
    }
}
