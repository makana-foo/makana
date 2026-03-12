using Makana.Api.Contracts;
using Makana.Journaling.Checkpointing;

namespace Makana.Api.Updates;

internal static class DeploymentSchemaPolicy
{
    public static int OldestSupportedVersion => DeploymentSchemaRegistry.OldestSupportedVersion;

    public static int LatestSupportedVersion => DeploymentSchemaRegistry.LatestSupportedVersion;

    public static int ValidateSupportedVersion(int version)
    {
        DeploymentSchemaRegistry.EnsureVersionSupported(version);
        return version;
    }

    public static int ResolvePersistedSchemaVersion(UntypedDeployment deployment)
    {
        var features = NormalizeFeatures(deployment.Features);
        var document = new UntypedDeploymentDocument(
            deployment.Version,
            features,
            deployment.Deployment.GetRawText());
        DeploymentSchemaRegistry.GetCodec(document.Version).ValidateEnvelope(document);
        return Math.Max(document.Version, DeploymentSchemaFeatures.GetRequiredVersion(features));
    }

    public static int ResolvePersistedSchemaVersion(string deploymentJson, int? preferredVersion = null)
    {
        var requiredVersion = InferRequiredVersion(deploymentJson);
        if (preferredVersion is null)
        {
            return requiredVersion;
        }

        return Math.Max(ValidateSupportedVersion(preferredVersion.Value), requiredVersion);
    }

    public static string[]? DescribeFeaturesForExport(string deploymentJson, int schemaVersion)
    {
        ValidateSupportedVersion(schemaVersion);
        var features = DetectFeatures(deploymentJson);
        var exportedFeatures = DeploymentSchemaFeatures.FilterFeaturesForVersion(schemaVersion, features);
        return exportedFeatures.Count == 0 ? null : [.. exportedFeatures];
    }

    private static int InferRequiredVersion(string deploymentJson)
        => DeploymentSchemaFeatures.GetRequiredVersion(DetectFeatures(deploymentJson));

    private static IReadOnlyList<string> DetectFeatures(string deploymentJson)
    {
        var snapshot = DeploymentSerializer.DeserializeUntypedDeployment(
            new UntypedDeploymentDocument(
                DeploymentSchemaVersions.OldestSupported,
                [],
                deploymentJson));
        return DeploymentSchemaFeatures.DetectDeploymentFeatures(snapshot.Resources);
    }

    private static IReadOnlyList<string> NormalizeFeatures(string[]? features)
        => features?
            .Where(static feature => !string.IsNullOrWhiteSpace(feature))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static feature => feature, StringComparer.Ordinal)
            .ToArray()
        ?? [];
}
