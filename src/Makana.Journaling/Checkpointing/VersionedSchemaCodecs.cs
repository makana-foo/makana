namespace Makana.Journaling.Checkpointing;

public static class DeploymentSchemaVersions
{
    public const int Current = 3;
    public const int OldestSupported = 3;
    public const int Latest = 4;
}

public static class DeploymentSchemaFeatures
{
    public const string RefreshBeforeUpdate = "refreshBeforeUpdate";
    public const string Views = "views";
    public const string Hooks = "hooks";
    public const string Taint = "taint";
    public const string ReplaceWith = "replaceWith";

    private static readonly IReadOnlyDictionary<string, int> MinimumVersionByFeature =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [RefreshBeforeUpdate] = DeploymentSchemaVersions.Latest,
            [Views] = DeploymentSchemaVersions.Latest,
            [Hooks] = DeploymentSchemaVersions.Latest,
            [Taint] = DeploymentSchemaVersions.Latest,
            [ReplaceWith] = DeploymentSchemaVersions.Latest,
        };

    public static IReadOnlyList<string> DetectDeploymentFeatures(IReadOnlyList<DeploymentResource> resources)
    {
        var features = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            if (resource.RefreshBeforeUpdate)
            {
                features.Add(RefreshBeforeUpdate);
            }

            if (!string.IsNullOrEmpty(resource.ViewOf))
            {
                features.Add(Views);
            }

            if (resource.ResourceHooks is { Count: > 0 })
            {
                features.Add(Hooks);
            }

            if (resource.Taint)
            {
                features.Add(Taint);
            }

            if (resource.ReplaceWith is { Count: > 0 })
            {
                features.Add(ReplaceWith);
            }
        }

        return [.. features.OrderBy(static feature => feature, StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> DetectCheckpointFeatures(IReadOnlyList<CheckpointResource> resources)
    {
        var features = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            if (resource.RefreshBeforeUpdate)
            {
                features.Add(RefreshBeforeUpdate);
            }

            if (!string.IsNullOrEmpty(resource.ViewOf))
            {
                features.Add(Views);
            }

            if (resource.ResourceHooks is { Count: > 0 })
            {
                features.Add(Hooks);
            }

            if (resource.Taint)
            {
                features.Add(Taint);
            }

            if (resource.ReplaceWith is { Count: > 0 })
            {
                features.Add(ReplaceWith);
            }
        }

        return [.. features.OrderBy(static feature => feature, StringComparer.Ordinal)];
    }

    public static int GetRequiredVersion(IReadOnlyList<string> features)
    {
        var version = DeploymentSchemaVersions.Current;
        foreach (var feature in features)
        {
            if (!MinimumVersionByFeature.TryGetValue(feature, out var minimumVersion))
            {
                continue;
            }

            version = Math.Max(version, minimumVersion);
        }

        return version;
    }

    public static IReadOnlyList<string> FilterFeaturesForVersion(int version, IReadOnlyList<string> features)
        => [.. features
            .Where(feature => MinimumVersionByFeature.TryGetValue(feature, out var minimumVersion) && minimumVersion <= version)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static feature => feature, StringComparer.Ordinal)];

    public static IReadOnlyList<string> GetUnsupportedFeatures(IReadOnlyList<string> features)
        => [.. features
            .Where(static feature => !MinimumVersionByFeature.ContainsKey(feature))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static feature => feature, StringComparer.Ordinal)];
}

public interface IDeploymentSchemaCodec
{
    int Version { get; }

    UntypedDeploymentDocument CreateEnvelope(string deploymentJson, IReadOnlyList<string> features);

    void ValidateEnvelope(UntypedDeploymentDocument deployment);
}

public interface ICheckpointSchemaCodec
{
    int Version { get; }

    VersionedCheckpointDocument CreateEnvelope(CheckpointSnapshot latest, IReadOnlyList<string> features);

    void ValidateEnvelope(int version, IReadOnlyList<string> features);
}

public sealed class DeploymentSchemaV3Codec : IDeploymentSchemaCodec
{
    public int Version => DeploymentSchemaVersions.Current;

    public UntypedDeploymentDocument CreateEnvelope(string deploymentJson, IReadOnlyList<string> features)
    {
        if (features.Count > 0)
        {
            throw new UnsupportedDeploymentFeaturesException(features);
        }

        return new UntypedDeploymentDocument(Version, [], deploymentJson);
    }

    public void ValidateEnvelope(UntypedDeploymentDocument deployment)
    {
        if (deployment.Features.Count > 0)
        {
            throw new UnsupportedDeploymentFeaturesException(deployment.Features);
        }
    }
}

public sealed class DeploymentSchemaV4Codec : IDeploymentSchemaCodec
{
    public int Version => DeploymentSchemaVersions.Latest;

    public UntypedDeploymentDocument CreateEnvelope(string deploymentJson, IReadOnlyList<string> features)
    {
        var unsupported = DeploymentSchemaFeatures.GetUnsupportedFeatures(features);
        if (unsupported.Count > 0)
        {
            throw new UnsupportedDeploymentFeaturesException(unsupported);
        }

        return new UntypedDeploymentDocument(Version, [.. features], deploymentJson);
    }

    public void ValidateEnvelope(UntypedDeploymentDocument deployment)
    {
        var unsupported = DeploymentSchemaFeatures.GetUnsupportedFeatures(deployment.Features);
        if (unsupported.Count > 0)
        {
            throw new UnsupportedDeploymentFeaturesException(unsupported);
        }
    }
}

public sealed class CheckpointSchemaV3Codec : ICheckpointSchemaCodec
{
    public int Version => DeploymentSchemaVersions.Current;

    public VersionedCheckpointDocument CreateEnvelope(CheckpointSnapshot latest, IReadOnlyList<string> features)
    {
        if (features.Count > 0)
        {
            throw new UnsupportedCheckpointFeaturesException(features);
        }

        return new VersionedCheckpointDocument(Version, [], latest);
    }

    public void ValidateEnvelope(int version, IReadOnlyList<string> features)
    {
        _ = version;
        if (features.Count > 0)
        {
            throw new UnsupportedCheckpointFeaturesException(features);
        }
    }
}

public sealed class CheckpointSchemaV4Codec : ICheckpointSchemaCodec
{
    public int Version => DeploymentSchemaVersions.Latest;

    public VersionedCheckpointDocument CreateEnvelope(CheckpointSnapshot latest, IReadOnlyList<string> features)
    {
        var unsupported = DeploymentSchemaFeatures.GetUnsupportedFeatures(features);
        if (unsupported.Count > 0)
        {
            throw new UnsupportedCheckpointFeaturesException(unsupported);
        }

        return new VersionedCheckpointDocument(Version, [.. features], latest);
    }

    public void ValidateEnvelope(int version, IReadOnlyList<string> features)
    {
        _ = version;
        var unsupported = DeploymentSchemaFeatures.GetUnsupportedFeatures(features);
        if (unsupported.Count > 0)
        {
            throw new UnsupportedCheckpointFeaturesException(unsupported);
        }
    }
}

public static class DeploymentSchemaRegistry
{
    private static readonly IReadOnlyDictionary<int, IDeploymentSchemaCodec> Codecs =
        new Dictionary<int, IDeploymentSchemaCodec>
        {
            [DeploymentSchemaVersions.Current] = new DeploymentSchemaV3Codec(),
            [DeploymentSchemaVersions.Latest] = new DeploymentSchemaV4Codec(),
        };

    public static int OldestSupportedVersion => DeploymentSchemaVersions.OldestSupported;

    public static int LatestSupportedVersion => DeploymentSchemaVersions.Latest;

    public static IDeploymentSchemaCodec GetCodec(int version)
    {
        EnsureVersionSupported(version);
        return Codecs[version];
    }

    public static IDeploymentSchemaCodec ResolveForFeatures(IReadOnlyList<string> features)
        => GetCodec(DeploymentSchemaFeatures.GetRequiredVersion(features));

    public static void EnsureVersionSupported(int version)
    {
        if (version > LatestSupportedVersion)
        {
            throw new DeploymentSchemaTooNewException();
        }

        if (version < OldestSupportedVersion)
        {
            throw new DeploymentSchemaTooOldException();
        }
    }
}

public static class CheckpointSchemaRegistry
{
    private static readonly IReadOnlyDictionary<int, ICheckpointSchemaCodec> Codecs =
        new Dictionary<int, ICheckpointSchemaCodec>
        {
            [DeploymentSchemaVersions.Current] = new CheckpointSchemaV3Codec(),
            [DeploymentSchemaVersions.Latest] = new CheckpointSchemaV4Codec(),
        };

    public static int OldestSupportedVersion => DeploymentSchemaVersions.OldestSupported;

    public static int LatestSupportedVersion => DeploymentSchemaVersions.Latest;

    public static ICheckpointSchemaCodec GetCodec(int version)
    {
        if (version > LatestSupportedVersion)
        {
            throw new InvalidOperationException($"unsupported checkpoint version {version}");
        }

        if (version < OldestSupportedVersion)
        {
            throw new InvalidOperationException($"unsupported checkpoint version {version}");
        }

        return Codecs[version];
    }

    public static ICheckpointSchemaCodec ResolveForFeatures(IReadOnlyList<string> features)
        => GetCodec(DeploymentSchemaFeatures.GetRequiredVersion(features));
}
