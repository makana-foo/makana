using System.Text.Json;
using System.Text.Json.Serialization;

namespace Makana.Journaling.Checkpointing;

public sealed record DeploymentCustomTimeouts(
    double? CreateSeconds = null,
    double? UpdateSeconds = null,
    double? DeleteSeconds = null);

public sealed record DeploymentResourceReference(
    string Urn,
    string? Id = null,
    string? PackageVersion = null);

public sealed record DeploymentAsset(
    string Kind,
    string? Text = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record DeploymentPropertyMap(
    IReadOnlyDictionary<string, CheckpointPropertyValue> Values);

public sealed record DeploymentResource(
    string Urn,
    string Type,
    bool Custom = false,
    bool Delete = false,
    string? Id = null,
    DeploymentPropertyMap? Inputs = null,
    DeploymentPropertyMap? Outputs = null,
    string? Parent = null,
    bool Protect = false,
    bool Taint = false,
    bool External = false,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyList<string>? InitErrors = null,
    string? Provider = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? PropertyDependencies = null,
    bool PendingReplacement = false,
    IReadOnlyList<string>? AdditionalSecretOutputs = null,
    IReadOnlyList<string>? Aliases = null,
    DeploymentCustomTimeouts? CustomTimeouts = null,
    string? ImportId = null,
    bool RetainOnDelete = false,
    string? DeletedWith = null,
    IReadOnlyList<string>? ReplaceWith = null,
    string? SourcePosition = null,
    IReadOnlyList<string>? HideDiff = null,
    IReadOnlyList<string>? IgnoreChanges = null,
    IReadOnlyList<string>? ReplaceOnChanges = null,
    CheckpointPropertyValue? ReplacementTrigger = null,
    bool RefreshBeforeUpdate = false,
    string? ViewOf = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ResourceHooks = null);

public sealed record DeploymentManifest(
    DateTimeOffset? Time = null,
    string? Magic = null,
    string? Version = null);

public sealed record DeploymentSnapshotDocument(
    DeploymentManifest? Manifest,
    IReadOnlyList<DeploymentResource> Resources,
    string? SecretsProviderType = null,
    IReadOnlyDictionary<string, CheckpointPropertyValue>? SecretsProviderState = null);

public sealed record UntypedDeploymentDocument(
    int Version,
    IReadOnlyList<string> Features,
    string DeploymentJson);

public sealed record SerializedDeploymentResource(
    [property: JsonPropertyName("urn")] string Urn,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("custom")] bool Custom,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("dependencies")] IReadOnlyList<string>? Dependencies,
    [property: JsonPropertyName("inputs")] IReadOnlyDictionary<string, object?> Inputs,
    [property: JsonPropertyName("outputs")] IReadOnlyDictionary<string, object?> Outputs,
    [property: JsonPropertyName("resourceHooks")] IReadOnlyDictionary<string, IReadOnlyList<string>>? ResourceHooks);

public sealed class DeploymentSchemaTooNewException()
    : InvalidOperationException("deployment schema version is too new");

public sealed class DeploymentSchemaTooOldException()
    : InvalidOperationException("deployment schema version is too old");

public sealed class UnsupportedDeploymentFeaturesException(IReadOnlyList<string> features)
    : InvalidOperationException($"unsupported deployment features: {string.Join(", ", features)}")
{
    public IReadOnlyList<string> Features { get; } = features;
}

public sealed class MissingSecretsManagerException()
    : InvalidOperationException("snapshot contains encrypted secrets but no secrets manager could be found");

public static class DeploymentSerializer
{
    public const int LatestDeploymentSchemaVersion = DeploymentSchemaVersions.Latest;
    public const int OldestSupportedDeploymentSchemaVersion = DeploymentSchemaVersions.OldestSupported;

    public static SerializedDeploymentResource SerializeResource(DeploymentResource resource, bool showSecrets)
        => new(
            Urn: resource.Urn,
            Type: resource.Type,
            Custom: resource.Custom,
            Id: resource.Id,
            Dependencies: resource.Dependencies?.ToArray(),
            Inputs: SerializePropertyMap(resource.Inputs, showSecrets),
            Outputs: SerializePropertyMap(resource.Outputs, showSecrets),
            ResourceHooks: resource.ResourceHooks is null
                ? null
                : new Dictionary<string, IReadOnlyList<string>>(resource.ResourceHooks, StringComparer.Ordinal));

    public static (DeploymentSnapshotDocument Deployment, int Version, IReadOnlyList<string> Features) SerializeDeploymentWithMetadata(
        DeploymentSnapshotDocument snapshot,
        bool showSecrets)
    {
        var features = DeploymentSchemaFeatures.DetectDeploymentFeatures(snapshot.Resources);
        var version = DeploymentSchemaRegistry.ResolveForFeatures(features).Version;
        return (SerializeDeployment(snapshot, showSecrets), version, features);
    }

    public static DeploymentSnapshotDocument SerializeDeployment(DeploymentSnapshotDocument snapshot, bool showSecrets)
        => snapshot;

    public static UntypedDeploymentDocument SerializeUntypedDeployment(DeploymentSnapshotDocument snapshot, bool showSecrets)
    {
        var (deployment, version, features) = SerializeDeploymentWithMetadata(snapshot, showSecrets);
        var deploymentJson = SerializeDeploymentDocument(deployment, showSecrets);
        return DeploymentSchemaRegistry.GetCodec(version).CreateEnvelope(deploymentJson, features);
    }

    public static DeploymentSnapshotDocument DeserializeUntypedDeployment(UntypedDeploymentDocument deployment)
    {
        DeploymentSchemaRegistry.GetCodec(deployment.Version).ValidateEnvelope(deployment);

        using var document = JsonDocument.Parse(deployment.DeploymentJson);
        var root = document.RootElement;

        var secretsProvider = TryReadSecretsProvider(root);
        var secretsManagerAvailable = secretsProvider.Type is not null;
        var resources = new List<DeploymentResource>();

        if (root.TryGetProperty("resources", out var resourcesElement))
        {
            if (resourcesElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("deployment resources must be an array");
            }

            foreach (var resourceElement in resourcesElement.EnumerateArray())
            {
                resources.Add(DeserializeResource(resourceElement, secretsManagerAvailable));
            }
        }

        return new DeploymentSnapshotDocument(
            Manifest: TryReadManifest(root),
            Resources: resources,
            SecretsProviderType: secretsProvider.Type,
            SecretsProviderState: secretsProvider.State);
    }

    public static string SerializePropertyValue(CheckpointPropertyValue value, bool showSecrets)
        => JsonSerializer.Serialize(CheckpointWireValueConverter.ToWireValue(value, showSecrets), CheckpointWireValueConverter.JsonOptions);

    public static CheckpointPropertyValue DeserializePropertyValue(object? value)
        => CheckpointWireValueConverter.FromWireValue(value, secretsManagerAvailable: true);

    public static void ValidatePropertyValueAgainstSchema(object? value)
        => CheckpointWireValueConverter.ValidateSchema(value);

    public static IReadOnlyDictionary<string, CheckpointPropertyValue>? DeserializeStackOutputs(UntypedDeploymentDocument deployment)
    {
        DeploymentSchemaRegistry.GetCodec(deployment.Version).ValidateEnvelope(deployment);

        using var document = JsonDocument.Parse(deployment.DeploymentJson);
        var root = document.RootElement;
        var secretsProvider = TryReadSecretsProvider(root);

        if (!root.TryGetProperty("resources", out var resourcesElement) || resourcesElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var resourceElement in resourcesElement.EnumerateArray())
        {
            if (!HasStringProperty(resourceElement, "type", "pulumi:pulumi:Stack"))
            {
                continue;
            }

            if (!resourceElement.TryGetProperty("outputs", out var outputsElement) || outputsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return DeserializePropertyMap(outputsElement, secretsProvider.Type is not null);
        }

        return null;
    }

    public static string SerializePropertyBagToJson(DeploymentPropertyMap propertyMap, bool showSecrets)
        => JsonSerializer.Serialize(SerializePropertyMap(propertyMap, showSecrets), CheckpointWireValueConverter.JsonOptions);

    public static string SerializeDeploymentDocument(DeploymentSnapshotDocument snapshot, bool showSecrets)
        => JsonSerializer.Serialize(ToWireDeploymentDocument(snapshot, showSecrets), CheckpointWireValueConverter.JsonOptions);

    private static object ToWireDeploymentDocument(DeploymentSnapshotDocument snapshot, bool showSecrets)
    {
        var root = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["manifest"] = snapshot.Manifest is null ? null : ToWireManifest(snapshot.Manifest),
            ["resources"] = snapshot.Resources.Select(resource => ToWireResource(resource, showSecrets)).ToArray(),
        };

        if (snapshot.SecretsProviderType is not null || snapshot.SecretsProviderState is not null)
        {
            root["secrets_providers"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["type"] = snapshot.SecretsProviderType ?? string.Empty,
                ["state"] = snapshot.SecretsProviderState is null
                    ? new Dictionary<string, object?>(StringComparer.Ordinal)
                    : SerializePropertyMap(new DeploymentPropertyMap(snapshot.SecretsProviderState), showSecrets),
            };
        }

        return root;
    }

    private static object ToWireManifest(DeploymentManifest manifest)
        => new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["time"] = manifest.Time,
            ["magic"] = manifest.Magic ?? string.Empty,
            ["version"] = manifest.Version ?? string.Empty,
        };

    private static object ToWireResource(DeploymentResource resource, bool showSecrets)
    {
        var wire = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["urn"] = resource.Urn,
            ["type"] = resource.Type,
            ["custom"] = resource.Custom,
        };

        AddIfNotNull(wire, "id", resource.Id);
        AddIfNotNull(wire, "parent", resource.Parent);
        AddIfNotNull(wire, "provider", resource.Provider);
        AddIfNotNull(wire, "viewOf", resource.ViewOf);
        AddIfNotNull(wire, "sourcePosition", resource.SourcePosition);
        AddIfNotNull(wire, "deletedWith", resource.DeletedWith);
        AddIfNotNull(wire, "importId", resource.ImportId);

        AddIf(wire, "delete", resource.Delete);
        AddIf(wire, "protect", resource.Protect);
        AddIf(wire, "taint", resource.Taint);
        AddIf(wire, "external", resource.External);
        AddIf(wire, "pendingReplacement", resource.PendingReplacement);
        AddIf(wire, "retainOnDelete", resource.RetainOnDelete);
        AddIf(wire, "refreshBeforeUpdate", resource.RefreshBeforeUpdate);

        AddIfNotNull(wire, "dependencies", resource.Dependencies?.ToArray());
        AddIfNotNull(wire, "initErrors", resource.InitErrors?.ToArray());
        AddIfNotNull(wire, "additionalSecretOutputs", resource.AdditionalSecretOutputs?.ToArray());
        AddIfNotNull(wire, "aliases", resource.Aliases?.ToArray());
        AddIfNotNull(wire, "replaceWith", resource.ReplaceWith?.ToArray());
        AddIfNotNull(wire, "hideDiff", resource.HideDiff?.ToArray());
        AddIfNotNull(wire, "ignoreChanges", resource.IgnoreChanges?.ToArray());
        AddIfNotNull(wire, "replaceOnChanges", resource.ReplaceOnChanges?.ToArray());

        if (resource.Inputs is not null)
        {
            wire["inputs"] = SerializePropertyMap(resource.Inputs, showSecrets);
        }

        if (resource.Outputs is not null)
        {
            wire["outputs"] = SerializePropertyMap(resource.Outputs, showSecrets);
        }

        if (resource.PropertyDependencies is not null)
        {
            wire["propertyDependencies"] = resource.PropertyDependencies.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Cast<object?>().ToArray(),
                StringComparer.Ordinal);
        }

        if (resource.ResourceHooks is not null)
        {
            wire["resourceHooks"] = resource.ResourceHooks.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Cast<object?>().ToArray(),
                StringComparer.Ordinal);
        }

        if (resource.CustomTimeouts is not null)
        {
            wire["customTimeouts"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["create"] = resource.CustomTimeouts.CreateSeconds,
                ["update"] = resource.CustomTimeouts.UpdateSeconds,
                ["delete"] = resource.CustomTimeouts.DeleteSeconds,
            };
        }

        if (resource.ReplacementTrigger is not null)
        {
            wire["replacementTrigger"] = CheckpointWireValueConverter.ToWireValue(resource.ReplacementTrigger, showSecrets);
        }

        return wire;
    }

    private static DeploymentManifest? TryReadManifest(JsonElement root)
    {
        if (!root.TryGetProperty("manifest", out var manifestElement) || manifestElement.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return new DeploymentManifest(
            Time: manifestElement.TryGetProperty("time", out var timeElement) && timeElement.ValueKind == JsonValueKind.String && timeElement.TryGetDateTimeOffset(out var time)
                ? time
                : null,
            Magic: manifestElement.TryGetProperty("magic", out var magicElement) && magicElement.ValueKind == JsonValueKind.String
                ? magicElement.GetString()
                : null,
            Version: manifestElement.TryGetProperty("version", out var versionElement) && versionElement.ValueKind == JsonValueKind.String
                ? versionElement.GetString()
                : null);
    }

    private static (string? Type, IReadOnlyDictionary<string, CheckpointPropertyValue>? State) TryReadSecretsProvider(JsonElement root)
    {
        if (!root.TryGetProperty("secrets_providers", out var providerElement) || providerElement.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        var type = providerElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : null;

        IReadOnlyDictionary<string, CheckpointPropertyValue>? state = null;
        if (providerElement.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.Object)
        {
            state = DeserializePropertyMap(stateElement, secretsManagerAvailable: true);
        }

        return (type, state);
    }

    private static DeploymentResource DeserializeResource(JsonElement resourceElement, bool secretsManagerAvailable)
    {
        var urn = RequireString(resourceElement, "urn");
        var type = RequireString(resourceElement, "type");
        var custom = resourceElement.TryGetProperty("custom", out var customElement) && customElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? customElement.GetBoolean()
            : false;

        var id = TryGetString(resourceElement, "id");
        if (!custom && !string.IsNullOrEmpty(id))
        {
            throw new InvalidOperationException("component resources may not carry IDs");
        }

        return new DeploymentResource(
            Urn: urn,
            Type: type,
            Custom: custom,
            Delete: TryGetBoolean(resourceElement, "delete"),
            Id: id,
            Inputs: resourceElement.TryGetProperty("inputs", out var inputsElement) && inputsElement.ValueKind == JsonValueKind.Object
                ? new DeploymentPropertyMap(DeserializePropertyMap(inputsElement, secretsManagerAvailable))
                : null,
            Outputs: resourceElement.TryGetProperty("outputs", out var outputsElement) && outputsElement.ValueKind == JsonValueKind.Object
                ? new DeploymentPropertyMap(DeserializePropertyMap(outputsElement, secretsManagerAvailable))
                : null,
            Parent: TryGetString(resourceElement, "parent"),
            Protect: TryGetBoolean(resourceElement, "protect"),
            Taint: TryGetBoolean(resourceElement, "taint"),
            External: TryGetBoolean(resourceElement, "external"),
            Dependencies: TryGetStringArray(resourceElement, "dependencies"),
            InitErrors: TryGetStringArray(resourceElement, "initErrors"),
            Provider: TryGetString(resourceElement, "provider"),
            PropertyDependencies: TryGetDependencyMap(resourceElement, "propertyDependencies"),
            PendingReplacement: TryGetBoolean(resourceElement, "pendingReplacement"),
            AdditionalSecretOutputs: TryGetStringArray(resourceElement, "additionalSecretOutputs"),
            Aliases: TryGetStringArray(resourceElement, "aliases"),
            CustomTimeouts: TryGetCustomTimeouts(resourceElement),
            ImportId: TryGetString(resourceElement, "importId"),
            RetainOnDelete: TryGetBoolean(resourceElement, "retainOnDelete"),
            DeletedWith: TryGetString(resourceElement, "deletedWith"),
            ReplaceWith: TryGetStringArray(resourceElement, "replaceWith"),
            SourcePosition: TryGetString(resourceElement, "sourcePosition"),
            HideDiff: TryGetStringArray(resourceElement, "hideDiff"),
            IgnoreChanges: TryGetStringArray(resourceElement, "ignoreChanges"),
            ReplaceOnChanges: TryGetStringArray(resourceElement, "replaceOnChanges"),
            ReplacementTrigger: resourceElement.TryGetProperty("replacementTrigger", out var replacementTriggerElement)
                ? CheckpointWireValueConverter.FromWireValue(replacementTriggerElement, secretsManagerAvailable)
                : null,
            RefreshBeforeUpdate: TryGetBoolean(resourceElement, "refreshBeforeUpdate"),
            ViewOf: TryGetString(resourceElement, "viewOf"),
            ResourceHooks: TryGetDependencyMap(resourceElement, "resourceHooks"));
    }

    private static Dictionary<string, object?> SerializePropertyMap(DeploymentPropertyMap? propertyMap, bool showSecrets)
        => propertyMap?.Values.ToDictionary(
            static pair => pair.Key,
            pair => CheckpointWireValueConverter.ToWireValue(pair.Value, showSecrets),
            StringComparer.Ordinal)
            ?? new Dictionary<string, object?>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, CheckpointPropertyValue> DeserializePropertyMap(JsonElement element, bool secretsManagerAvailable)
    {
        var properties = new Dictionary<string, CheckpointPropertyValue>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            properties[property.Name] = CheckpointWireValueConverter.FromWireValue(property.Value, secretsManagerAvailable);
        }

        return properties;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? TryGetDependencyMap(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var mapElement) || mapElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var property in mapElement.EnumerateObject())
        {
            result[property.Name] = property.Value.ValueKind == JsonValueKind.Null
                ? Array.Empty<string>()
                : property.Value.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray();
        }

        return result;
    }

    private static DeploymentCustomTimeouts? TryGetCustomTimeouts(JsonElement element)
    {
        if (!element.TryGetProperty("customTimeouts", out var timeoutsElement) || timeoutsElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new DeploymentCustomTimeouts(
            CreateSeconds: TryGetDouble(timeoutsElement, "create"),
            UpdateSeconds: TryGetDouble(timeoutsElement, "update"),
            DeleteSeconds: TryGetDouble(timeoutsElement, "delete"));
    }

    private static bool HasStringProperty(JsonElement element, string name, string expected)
        => element.TryGetProperty(name, out var valueElement)
            && valueElement.ValueKind == JsonValueKind.String
            && string.Equals(valueElement.GetString(), expected, StringComparison.Ordinal);

    private static string RequireString(JsonElement element, string propertyName)
        => TryGetString(element, propertyName) ?? throw new InvalidOperationException($"resource is missing required string property '{propertyName}'");

    private static string? TryGetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : null;

    private static bool TryGetBoolean(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? propertyElement.GetBoolean()
            : false;

    private static double? TryGetDouble(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.Number
            ? propertyElement.GetDouble()
            : null;

    private static IReadOnlyList<string>? TryGetStringArray(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.Array
            ? propertyElement.EnumerateArray().Select(static item => item.GetString() ?? string.Empty).ToArray()
            : null;

    private static void AddIfNotNull(Dictionary<string, object?> dictionary, string key, object? value)
    {
        if (value is not null)
        {
            dictionary[key] = value;
        }
    }

    private static void AddIf(Dictionary<string, object?> dictionary, string key, bool value)
    {
        if (value)
        {
            dictionary[key] = true;
        }
    }
}

internal static class CheckpointWireValueConverter
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
    };

    private const string SecretSig = "secret";
    private const string AssetSig = "asset";
    private const string ResourceReferenceSig = "resource-reference";
    private const string ComputedSig = "computed";
    private const string OutputSig = "output";
    private const string SpecialNumberSig = "special-number";

    public static object? ToWireValue(CheckpointPropertyValue value, bool showSecrets)
    {
        return value.Kind switch
        {
            CheckpointPropertyKind.Null => null,
            CheckpointPropertyKind.String => value.StringValue,
            CheckpointPropertyKind.Boolean => value.BooleanValue,
            CheckpointPropertyKind.Number => SerializeNumber(value.NumberValue ?? 0),
            CheckpointPropertyKind.Array => value.ArrayValue?.Select(item => ToWireValue(item, showSecrets)).ToArray(),
            CheckpointPropertyKind.Object => SerializeObject(value.ObjectValue, showSecrets),
            _ => throw new InvalidOperationException($"unsupported checkpoint property kind '{value.Kind}'"),
        };
    }

    public static CheckpointPropertyValue FromWireValue(object? value, bool secretsManagerAvailable)
    {
        value = Normalize(value);

        return value switch
        {
            null => CheckpointPropertyValue.Null(),
            string stringValue => CheckpointPropertyValue.String(stringValue),
            bool booleanValue => CheckpointPropertyValue.Boolean(booleanValue),
            double doubleValue => CheckpointPropertyValue.Number(doubleValue),
            int intValue => CheckpointPropertyValue.Number(intValue),
            long longValue => CheckpointPropertyValue.Number(longValue),
            Dictionary<string, object?> dictionaryValue => DeserializeObject(dictionaryValue, secretsManagerAvailable),
            List<object?> listValue => CheckpointPropertyValue.Array(listValue.Select(item => FromWireValue(item, secretsManagerAvailable)).ToArray()),
            _ => throw new InvalidOperationException($"unsupported property value type '{value.GetType()}'"),
        };
    }

    public static void ValidateSchema(object? value)
    {
        value = Normalize(value);

        switch (value)
        {
            case null:
            case string:
            case bool:
            case double:
            case int:
            case long:
                return;
            case List<object?> list:
                foreach (var item in list)
                {
                    ValidateSchema(item);
                }

                return;
            case object?[] array:
                foreach (var item in array)
                {
                    ValidateSchema(item);
                }

                return;
            case Dictionary<string, object?> dictionary:
                ValidateDictionarySchema(dictionary);
                return;
            default:
                throw new InvalidOperationException($"unsupported wire property value '{value.GetType()}'");
        }
    }

    private static object? Normalize(object? value)
    {
        return value switch
        {
            JsonElement element => NormalizeJsonElement(element),
            JsonDocument document => NormalizeJsonElement(document.RootElement),
            object?[] array => array.Select(Normalize).ToList(),
            _ => value,
        };
    }

    private static object? NormalizeJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDouble(),
            JsonValueKind.Array => element.EnumerateArray().Select(NormalizeJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(
                static property => property.Name,
                property => NormalizeJsonElement(property.Value),
                StringComparer.Ordinal),
            _ => throw new InvalidOperationException($"unsupported JSON kind '{element.ValueKind}'"),
        };
    }

    private static object SerializeNumber(double number)
    {
        if (double.IsNaN(number))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sig"] = SpecialNumberSig,
                ["value"] = "NaN",
            };
        }

        if (double.IsPositiveInfinity(number))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sig"] = SpecialNumberSig,
                ["value"] = "Infinity",
            };
        }

        if (double.IsNegativeInfinity(number))
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sig"] = SpecialNumberSig,
                ["value"] = "-Infinity",
            };
        }

        return number;
    }

    private static object SerializeObject(IReadOnlyDictionary<string, CheckpointPropertyValue>? objectValue, bool showSecrets)
    {
        var dictionary = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (objectValue is null)
        {
            return dictionary;
        }

        foreach (var (key, propertyValue) in objectValue)
        {
            dictionary[key] = ToWireValue(propertyValue, showSecrets);
        }

        return dictionary;
    }

    private static CheckpointPropertyValue DeserializeObject(Dictionary<string, object?> dictionary, bool secretsManagerAvailable)
    {
        if (dictionary.TryGetValue("sig", out var sigValue) && sigValue is string sig)
        {
            return sig switch
            {
                SecretSig => DeserializeSecret(dictionary, secretsManagerAvailable),
                AssetSig => ToCheckpointObject(dictionary, secretsManagerAvailable),
                ResourceReferenceSig => ToCheckpointObject(dictionary, secretsManagerAvailable),
                ComputedSig => ToCheckpointObject(dictionary, secretsManagerAvailable),
                OutputSig => ToCheckpointObject(dictionary, secretsManagerAvailable),
                SpecialNumberSig => DeserializeSpecialNumber(dictionary),
                _ => throw new InvalidOperationException($"unknown signature '{sig}'"),
            };
        }

        return ToCheckpointObject(dictionary, secretsManagerAvailable);
    }

    private static CheckpointPropertyValue DeserializeSecret(Dictionary<string, object?> dictionary, bool secretsManagerAvailable)
    {
        if (dictionary.ContainsKey("plaintext"))
        {
            return ToCheckpointObject(dictionary, secretsManagerAvailable);
        }

        if (dictionary.ContainsKey("ciphertext"))
        {
            if (!secretsManagerAvailable)
            {
                throw new MissingSecretsManagerException();
            }

            return ToCheckpointObject(dictionary, secretsManagerAvailable);
        }

        throw new InvalidOperationException("secret values must contain either plaintext or ciphertext");
    }

    private static CheckpointPropertyValue DeserializeSpecialNumber(Dictionary<string, object?> dictionary)
    {
        var encoded = dictionary.TryGetValue("value", out var value) ? value as string : null;
        return encoded switch
        {
            "NaN" => CheckpointPropertyValue.Number(double.NaN),
            "Infinity" => CheckpointPropertyValue.Number(double.PositiveInfinity),
            "-Infinity" => CheckpointPropertyValue.Number(double.NegativeInfinity),
            _ => throw new InvalidOperationException("unknown special numeric value"),
        };
    }

    private static CheckpointPropertyValue ToCheckpointObject(Dictionary<string, object?> dictionary, bool secretsManagerAvailable)
        => CheckpointPropertyValue.Object(dictionary.ToDictionary(
            static pair => pair.Key,
            pair => FromWireValue(pair.Value, secretsManagerAvailable),
            StringComparer.Ordinal));

    private static void ValidateDictionarySchema(Dictionary<string, object?> dictionary)
    {
        if (dictionary.TryGetValue("sig", out var sigValue))
        {
            if (sigValue is not string sig)
            {
                throw new InvalidOperationException("signature values must be strings");
            }

            if (sig is not SecretSig and not AssetSig and not ResourceReferenceSig and not ComputedSig and not OutputSig and not SpecialNumberSig)
            {
                throw new InvalidOperationException($"unknown signature '{sig}'");
            }

            if (sig == SecretSig && !dictionary.ContainsKey("plaintext") && !dictionary.ContainsKey("ciphertext"))
            {
                throw new InvalidOperationException("secret values must include plaintext or ciphertext");
            }

            if (sig == ResourceReferenceSig && !dictionary.ContainsKey("urn"))
            {
                throw new InvalidOperationException("resource references must include urn");
            }

            if (sig == SpecialNumberSig && !dictionary.ContainsKey("value"))
            {
                throw new InvalidOperationException("special numbers must include value");
            }
        }

        foreach (var (_, nestedValue) in dictionary)
        {
            ValidateSchema(nestedValue);
        }
    }
}
