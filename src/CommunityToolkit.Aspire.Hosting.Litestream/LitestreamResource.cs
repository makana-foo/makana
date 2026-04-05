using System.Text;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// Represents a Litestream sidecar resource that can replicate one or more SQLite database files or directories.
/// </summary>
/// <param name="name">The resource name.</param>
public sealed class LitestreamResource(string name) : ContainerResource(name)
{
    internal const string ConfigurationFileTargetPath = "/etc/litestream.yml";
    internal const string DefaultDatabaseRootPath = "/var/lib/litestream";

    private const string AzureAccountNameEnvironmentVariable = "LITESTREAM_AZURE_ACCOUNT_NAME";
    private const string S3EndpointEnvironmentVariable = "LITESTREAM_S3_ENDPOINT";
    private readonly List<LitestreamDatabaseConfiguration> _databaseConfigurations = [];
    private string _databaseRootPath = DefaultDatabaseRootPath;
    private LitestreamReplicaConfiguration? _pendingReplicaConfiguration;

    internal string DatabaseRootPath
    {
        get => _databaseRootPath;
        set => _databaseRootPath = NormalizePath(value);
    }

    /// <summary>
    /// Gets the configured single database path when exactly one single-database scope is registered.
    /// </summary>
    public string? DatabasePath => _databaseConfigurations is [{ Mode: LitestreamDatabaseMode.Single } singleDatabase]
        ? singleDatabase.Path
        : null;

    /// <summary>
    /// Gets the configured directory path when exactly one directory scope is registered.
    /// </summary>
    public string? DatabaseDirectoryPath => _databaseConfigurations is [{ Mode: LitestreamDatabaseMode.Directory } directoryDatabase]
        ? directoryDatabase.Path
        : null;

    internal void ConfigureSingleDatabase(string databasePath, string? replicaPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        AddDatabaseConfiguration(
            LitestreamDatabaseMode.Single,
            databasePath,
            pattern: null,
            recursive: false,
            watch: false,
            replicaPath);
    }

    internal void ConfigureDirectory(string directoryPath, string pattern, bool recursive, bool watch, string? replicaPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        AddDatabaseConfiguration(
            LitestreamDatabaseMode.Directory,
            directoryPath,
            pattern,
            recursive,
            watch,
            replicaPath);
    }

    internal void ConfigureS3Replica(string bucket, string path, string? region, bool forcePathStyle, bool skipVerify, bool hasEndpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _pendingReplicaConfiguration = new LitestreamReplicaConfiguration(
            LitestreamReplicaProviderType.S3,
            bucket,
            NormalizePath(path),
            region,
            hasEndpoint,
            forcePathStyle || hasEndpoint,
            skipVerify);

        ApplyPendingReplicaConfiguration();
    }

    internal void ConfigureAzureBlobReplica(string containerName, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        _pendingReplicaConfiguration = new LitestreamReplicaConfiguration(
            LitestreamReplicaProviderType.AzureBlobStorage,
            containerName,
            NormalizePath(path),
            null,
            false,
            false,
            false);

        ApplyPendingReplicaConfiguration();
    }

    internal string RenderConfiguration()
    {
        ValidateConfiguration();

        StringBuilder builder = new();
        builder.AppendLine("dbs:");

        foreach (var database in _databaseConfigurations)
        {
            builder.AppendLine("  -");

            if (database.Mode == LitestreamDatabaseMode.Single)
            {
                builder.AppendLine($"    path: {Quote(database.Path)}");
            }
            else
            {
                builder.AppendLine($"    dir: {Quote(database.Path)}");
                builder.AppendLine($"    pattern: {Quote(database.Pattern)}");
                builder.AppendLine($"    recursive: {YamlBool(database.Recursive)}");
                builder.AppendLine($"    watch: {YamlBool(database.Watch)}");
            }

            RenderReplicaConfiguration(builder, database.ReplicaConfiguration!);
        }

        return builder.ToString();
    }

    private void AddDatabaseConfiguration(
        LitestreamDatabaseMode mode,
        string configuredPath,
        string? pattern,
        bool recursive,
        bool watch,
        string? replicaPath)
    {
        _databaseConfigurations.Add(new LitestreamDatabaseConfiguration(
            mode,
            ResolveDatabasePath(configuredPath),
            pattern,
            recursive,
            watch,
            replicaPath,
            ResolveReplicaConfiguration(replicaPath)));
    }

    private void ApplyPendingReplicaConfiguration()
    {
        if (_pendingReplicaConfiguration is null)
        {
            return;
        }

        for (var index = 0; index < _databaseConfigurations.Count; index++)
        {
            var configuration = _databaseConfigurations[index];
            _databaseConfigurations[index] = configuration with
            {
                ReplicaConfiguration = ResolveReplicaConfiguration(configuration.ReplicaPath)
            };
        }
    }

    private LitestreamReplicaConfiguration? ResolveReplicaConfiguration(string? replicaPath)
    {
        if (_pendingReplicaConfiguration is null)
        {
            return null;
        }

        var resolvedReplicaPath = string.IsNullOrWhiteSpace(replicaPath)
            ? _pendingReplicaConfiguration.Path
            : NormalizePath(Path.Join(_pendingReplicaConfiguration.Path, replicaPath));

        return _pendingReplicaConfiguration with { Path = resolvedReplicaPath };
    }

    private static void RenderReplicaConfiguration(StringBuilder builder, LitestreamReplicaConfiguration replicaConfiguration)
    {
        builder.AppendLine("    replica:");

        if (replicaConfiguration.ProviderType == LitestreamReplicaProviderType.S3)
        {
            builder.AppendLine("      type: s3");
            builder.AppendLine($"      bucket: {Quote(replicaConfiguration.Bucket)}");
            builder.AppendLine($"      path: {Quote(replicaConfiguration.Path)}");

            if (!string.IsNullOrWhiteSpace(replicaConfiguration.Region))
            {
                builder.AppendLine($"      region: {Quote(replicaConfiguration.Region)}");
            }

            if (replicaConfiguration.ForcePathStyle)
            {
                builder.AppendLine("      force-path-style: true");
            }

            if (replicaConfiguration.SkipVerify)
            {
                builder.AppendLine("      skip-verify: true");
            }

            if (replicaConfiguration.HasCustomS3Endpoint)
            {
                builder.AppendLine($"      endpoint: ${{{S3EndpointEnvironmentVariable}}}");
            }

            return;
        }

        builder.AppendLine("      type: abs");
        builder.AppendLine($"      account-name: ${{{AzureAccountNameEnvironmentVariable}}}");
        builder.AppendLine($"      bucket: {Quote(replicaConfiguration.Bucket)}");
        builder.AppendLine($"      path: {Quote(replicaConfiguration.Path)}");
    }

    private void ValidateConfiguration()
    {
        if (_databaseConfigurations.Count == 0)
        {
            throw new InvalidOperationException($"Litestream resource '{Name}' requires at least one database path or database directory.");
        }

        if (_databaseConfigurations.Any(static configuration => configuration.ReplicaConfiguration is null))
        {
            throw new InvalidOperationException(
                $"Litestream resource '{Name}' requires a replica provider configuration for every configured database scope.");
        }
    }

    private string ResolveDatabasePath(string configuredPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return NormalizePath(configuredPath);
        }

        return NormalizePath(Path.Join(DatabaseRootPath, configuredPath));
    }

    private static string NormalizePath(string value)
        => value.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string YamlBool(bool value)
        => value ? "true" : "false";

    private static string Quote(string? value)
        => $"'{value?.Replace("'", "''", StringComparison.Ordinal) ?? string.Empty}'";
}

internal enum LitestreamDatabaseMode
{
    Single,
    Directory
}

internal enum LitestreamReplicaProviderType
{
    S3,
    AzureBlobStorage
}

internal sealed record LitestreamDatabaseConfiguration(
    LitestreamDatabaseMode Mode,
    string Path,
    string? Pattern,
    bool Recursive,
    bool Watch,
    string? ReplicaPath,
    LitestreamReplicaConfiguration? ReplicaConfiguration);

internal sealed record LitestreamReplicaConfiguration(
    LitestreamReplicaProviderType ProviderType,
    string Bucket,
    string Path,
    string? Region,
    bool HasCustomS3Endpoint,
    bool ForcePathStyle,
    bool SkipVerify);
