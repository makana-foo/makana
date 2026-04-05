namespace Makana.Journaling.Checkpointing;

public sealed record LegacyConfigEntry(string Value, bool Secret = false);

public sealed record CheckpointV1Document(
    string Stack,
    IReadOnlyDictionary<string, LegacyConfigEntry> Config,
    DeploymentSnapshotDocument? Latest);

public sealed record CheckpointV2Document(
    string Stack,
    IReadOnlyDictionary<string, LegacyConfigEntry> Config,
    DeploymentSnapshotDocument? Latest);

public sealed record LegacyDeploymentV1Document(
    DeploymentManifest Manifest,
    IReadOnlyList<DeploymentResource> Resources);

public sealed record LegacyDeploymentV2Document(
    DeploymentManifest Manifest,
    IReadOnlyList<DeploymentResource> Resources);

public sealed record LegacyResourceV1Document(
    string Urn,
    bool Custom,
    bool Delete,
    string? Id,
    string Type,
    IReadOnlyDictionary<string, object?> Inputs,
    IReadOnlyDictionary<string, object?> Defaults,
    IReadOnlyDictionary<string, object?> Outputs,
    string? Parent,
    bool Protect,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string>? InitErrors = null);

public sealed record LegacyResourceV2Document(
    string Urn,
    bool Custom,
    bool Delete,
    string? Id,
    string Type,
    IReadOnlyDictionary<string, object?> Inputs,
    IReadOnlyDictionary<string, object?> Outputs,
    string? Parent,
    bool Protect,
    bool External,
    IReadOnlyList<string> Dependencies,
    string Provider,
    IReadOnlyList<string>? InitErrors = null);

public sealed record LegacyResourceV3Document(
    string Urn,
    bool Custom,
    bool Delete,
    string? Id,
    string Type,
    IReadOnlyDictionary<string, object?> Inputs,
    IReadOnlyDictionary<string, object?> Outputs,
    string? Parent,
    bool Protect,
    bool External,
    IReadOnlyList<string> Dependencies,
    string Provider,
    IReadOnlyList<string>? InitErrors,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? PropertyDependencies,
    DateTimeOffset? Created,
    DateTimeOffset? Modified);

public static class CheckpointMigrator
{
    public static CheckpointV2Document UpgradeToV2(CheckpointV1Document checkpoint)
        => new(
            Stack: checkpoint.Stack,
            Config: new Dictionary<string, LegacyConfigEntry>(checkpoint.Config, StringComparer.Ordinal),
            Latest: checkpoint.Latest is null
                ? null
                : new DeploymentSnapshotDocument(
                    Manifest: checkpoint.Latest.Manifest,
                    Resources: [.. checkpoint.Latest.Resources],
                    SecretsProviderType: checkpoint.Latest.SecretsProviderType,
                    SecretsProviderState: checkpoint.Latest.SecretsProviderState is null
                        ? null
                        : new Dictionary<string, CheckpointPropertyValue>(checkpoint.Latest.SecretsProviderState, StringComparer.Ordinal)));

    public static LegacyDeploymentV2Document UpgradeDeploymentToV2(LegacyDeploymentV1Document deployment)
        => new(
            Manifest: deployment.Manifest,
            Resources: [.. deployment.Resources]);

    public static LegacyResourceV2Document UpgradeResourceToV2(LegacyResourceV1Document resource)
        => new(
            Urn: resource.Urn,
            Custom: resource.Custom,
            Delete: resource.Delete,
            Id: resource.Id,
            Type: resource.Type,
            Inputs: new Dictionary<string, object?>(resource.Inputs, StringComparer.Ordinal),
            Outputs: new Dictionary<string, object?>(resource.Outputs, StringComparer.Ordinal),
            Parent: resource.Parent,
            Protect: resource.Protect,
            External: false,
            Dependencies: [.. resource.Dependencies],
            Provider: string.Empty,
            InitErrors: resource.InitErrors?.ToArray());

    public static LegacyResourceV3Document UpgradeResourceToV3(LegacyResourceV2Document resource)
        => new(
            Urn: resource.Urn,
            Custom: resource.Custom,
            Delete: resource.Delete,
            Id: resource.Id,
            Type: resource.Type,
            Inputs: new Dictionary<string, object?>(resource.Inputs, StringComparer.Ordinal),
            Outputs: new Dictionary<string, object?>(resource.Outputs, StringComparer.Ordinal),
            Parent: resource.Parent,
            Protect: resource.Protect,
            External: resource.External,
            Dependencies: [.. resource.Dependencies],
            Provider: resource.Provider,
            InitErrors: resource.InitErrors?.ToArray(),
            PropertyDependencies: null,
            Created: null,
            Modified: null);
}
