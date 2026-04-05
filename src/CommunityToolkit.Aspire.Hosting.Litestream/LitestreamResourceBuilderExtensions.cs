using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Provides extension methods for configuring Litestream sidecar resources.
/// </summary>
public static class LitestreamResourceBuilderExtensions
{
    private const string S3AccessKeyEnvironmentVariable = "LITESTREAM_ACCESS_KEY_ID";
    private const string S3SecretKeyEnvironmentVariable = "LITESTREAM_SECRET_ACCESS_KEY";
    private const string S3EndpointEnvironmentVariable = "LITESTREAM_S3_ENDPOINT";

    /// <summary>
    /// Adds a Litestream sidecar resource to the application model.
    /// </summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">The resource name.</param>
    /// <returns>A resource builder for the Litestream resource.</returns>
    public static IResourceBuilder<LitestreamResource> AddLitestream(this IDistributedApplicationBuilder builder, [ResourceName] string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        LitestreamResource resource = new(name);

        return builder.AddResource(resource)
            .WithImage(LitestreamContainerImageTags.Image, LitestreamContainerImageTags.Tag)
            .WithImageRegistry(LitestreamContainerImageTags.Registry)
            .WithDockerfile("../Litestream")
            .WithArgs("replicate", "-config", LitestreamResource.ConfigurationFileTargetPath);
    }

    /// <summary>
    /// Configures the runtime user baked into the Litestream sidecar image for this resource.
    /// </summary>
    /// <param name="builder">The Litestream resource builder.</param>
    /// <param name="userId">The numeric user ID.</param>
    /// <param name="groupId">The numeric group ID.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<LitestreamResource> WithContainerUser(
        this IResourceBuilder<LitestreamResource> builder,
        int userId,
        int groupId)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithBuildArg("UID", userId.ToString())
            .WithBuildArg("GID", groupId.ToString());
    }

    /// <summary>
    /// Writes the rendered Litestream configuration into the container after databases and replicas are configured.
    /// </summary>
    /// <param name="builder">The Litestream resource builder.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<LitestreamResource> WithGeneratedConfigurationFile(
        this IResourceBuilder<LitestreamResource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithContainerFiles(
            "/etc",
            [
                new ContainerFile
                {
                    Name = "litestream.yml",
                    Contents = builder.Resource.RenderConfiguration()
                }
            ]);
    }

    /// <summary>
    /// Configures Litestream to replicate a single database file.
    /// </summary>
    /// <param name="builder">The Litestream resource builder.</param>
    /// <param name="databasePath">The database path relative to the Litestream data root, or an absolute container path.</param>
    /// <param name="replicaPath">An optional path relative to the configured replica root for this database scope.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<LitestreamResource> WithDatabase(
        this IResourceBuilder<LitestreamResource> builder,
        string databasePath,
        string? replicaPath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.ConfigureSingleDatabase(databasePath, replicaPath);
        return builder;
    }

    /// <summary>
    /// Configures Litestream to replicate a directory of SQLite databases.
    /// </summary>
    /// <param name="builder">The Litestream resource builder.</param>
    /// <param name="directoryPath">The directory path relative to the Litestream data root, or an absolute container path.</param>
    /// <param name="pattern">The glob pattern used to match database files.</param>
    /// <param name="recursive">A value indicating whether subdirectories should be scanned recursively.</param>
    /// <param name="watch">A value indicating whether directory changes should be watched for new databases.</param>
    /// <param name="replicaPath">An optional path relative to the configured replica root for this directory scope.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<LitestreamResource> WithDatabaseDirectory(
        this IResourceBuilder<LitestreamResource> builder,
        string directoryPath,
        string pattern = "*.db",
        bool recursive = false,
        bool watch = false,
        string? replicaPath = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.ConfigureDirectory(directoryPath, pattern, recursive, watch, replicaPath);
        return builder;
    }

    /// <summary>
    /// Adds a named volume for Litestream-managed SQLite files.
    /// </summary>
    /// <param name="builder">The Litestream resource builder.</param>
    /// <param name="name">The optional volume name.</param>
    /// <param name="target">The container path that Litestream will use for SQLite files.</param>
    /// <param name="isReadOnly">A value indicating whether the volume should be mounted read-only.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<LitestreamResource> WithDataVolume(
        this IResourceBuilder<LitestreamResource> builder,
        string? name = null,
        string target = LitestreamResource.DefaultDatabaseRootPath,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Resource.DatabaseRootPath = target;
        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), target, isReadOnly);
    }

    /// <summary>
    /// Adds a bind mount for Litestream-managed SQLite files.
    /// </summary>
    /// <param name="builder">The Litestream resource builder.</param>
    /// <param name="source">The host path to mount into the Litestream container.</param>
    /// <param name="target">The container path that Litestream will use for SQLite files.</param>
    /// <param name="isReadOnly">A value indicating whether the bind mount should be mounted read-only.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<LitestreamResource> WithDataBindMount(
        this IResourceBuilder<LitestreamResource> builder,
        string source,
        string target = LitestreamResource.DefaultDatabaseRootPath,
        bool isReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(source);

        builder.Resource.DatabaseRootPath = target;
        return builder.WithBindMount(source, target, isReadOnly);
    }

    /// <summary>
    /// Configures a generic S3-compatible replica target.
    /// </summary>
    /// <param name="builder">The Litestream resource builder.</param>
    /// <param name="bucket">The target bucket name.</param>
    /// <param name="path">The target path within the bucket.</param>
    /// <param name="region">The optional S3 region.</param>
    /// <param name="endpoint">The optional endpoint resource.</param>
    /// <param name="endpointParameter">The optional endpoint parameter used for deployment-time endpoint injection.</param>
    /// <param name="accessKey">The optional S3 access key parameter.</param>
    /// <param name="secretKey">The optional S3 secret key parameter.</param>
    /// <param name="forcePathStyle">A value indicating whether path-style S3 URLs should be used.</param>
    /// <param name="skipVerify">A value indicating whether TLS certificate validation should be skipped.</param>
    /// <returns>The resource builder.</returns>
    public static IResourceBuilder<LitestreamResource> WithS3Replica(
        this IResourceBuilder<LitestreamResource> builder,
        string bucket,
        string path,
        string? region = null,
        EndpointReference? endpoint = null,
        IResourceBuilder<ParameterResource>? endpointParameter = null,
        IResourceBuilder<ParameterResource>? accessKey = null,
        IResourceBuilder<ParameterResource>? secretKey = null,
        bool forcePathStyle = false,
        bool skipVerify = false)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (endpoint is not null && endpointParameter is not null)
        {
            throw new InvalidOperationException("Specify either an endpoint resource or an endpoint parameter, but not both.");
        }

        var hasEndpoint = endpoint is not null || endpointParameter is not null;
        builder.Resource.ConfigureS3Replica(bucket, path, region, forcePathStyle, skipVerify, hasEndpoint);

        if (endpoint is not null)
        {
            builder.WithEnvironment(S3EndpointEnvironmentVariable, endpoint);
        }

        if (endpointParameter is not null)
        {
            builder.WithEnvironment(S3EndpointEnvironmentVariable, endpointParameter.Resource);
        }

        if (accessKey is not null)
        {
            builder.WithEnvironment(S3AccessKeyEnvironmentVariable, accessKey.Resource);
        }

        if (secretKey is not null)
        {
            builder.WithEnvironment(S3SecretKeyEnvironmentVariable, secretKey.Resource);
        }

        return builder;
    }
}
