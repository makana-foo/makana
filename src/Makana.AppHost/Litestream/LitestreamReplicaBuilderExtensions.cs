using Aspire.Hosting.ApplicationModel;

namespace Makana.AppHost.Litestream;

internal static class LitestreamReplicaBuilderExtensions
{
    private const string LocalReplicaEndpoint = "http://s3.dev.internal:9000";

    public static async Task<LocalS3ReplicaResources?> AddLocalS3IfNeededAsync(
        this IDistributedApplicationBuilder builder,
        LitestreamReplicaParameters primaryParameters,
        LitestreamReplicaParameters secondaryParameters,
        CancellationToken cancellationToken = default)
    {
        if (builder.ExecutionContext.IsPublishMode)
        {
            return null;
        }

        var primaryAccessKey = await primaryParameters.GetAccessKeyAsync(cancellationToken);
        var primarySecretKey = await primaryParameters.GetSecretKeyAsync(cancellationToken);
        var secondaryAccessKey = await secondaryParameters.GetAccessKeyAsync(cancellationToken);
        var secondarySecretKey = await secondaryParameters.GetSecretKeyAsync(cancellationToken);

        if (!string.Equals(primaryAccessKey, secondaryAccessKey, StringComparison.Ordinal) ||
            !string.Equals(primarySecretKey, secondarySecretKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Local Litestream replicas require the same access key and secret key across resources.");
        }

        var bucketNames = new[]
        {
            await primaryParameters.GetBucketAsync(cancellationToken),
            await secondaryParameters.GetBucketAsync(cancellationToken)
        }
        .Distinct(StringComparer.Ordinal)
        .ToArray();

        var minio = builder.AddContainer("s3", "minio/minio")
            .WithHttpEndpoint(port: 3900, targetPort: 9000)
            .WithHttpEndpoint(port: 3901, targetPort: 9001, name: "console")
            .WithEnvironment("MINIO_ROOT_USER", primaryParameters.AccessKey.Resource)
            .WithEnvironment("MINIO_ROOT_PASSWORD", primaryParameters.SecretKey.Resource)
            .WithVolume("s3-data", "/data")
            .WithArgs("server", "/data", "--console-address", ":9001")
            .WithHttpHealthCheck(path: "/minio/health/live");

        var bucketInitializer = builder.AddContainer("s3-init", "minio/mc")
            .WithEntrypoint("/bin/sh")
            .WithEnvironment("MINIO_ROOT_USER", primaryParameters.AccessKey.Resource)
            .WithEnvironment("MINIO_ROOT_PASSWORD", primaryParameters.SecretKey.Resource)
            .WithArgs(
                "-ceu",
                BuildBucketInitializationScript(bucketNames))
            .WaitFor(minio);

        return new LocalS3ReplicaResources(minio, bucketInitializer);
    }

    public static async Task<IResourceBuilder<LitestreamResource>> WithConfiguredS3ReplicaAsync(
        this IResourceBuilder<LitestreamResource> litestream,
        IDistributedApplicationBuilder builder,
        LitestreamReplicaParameters parameters,
        LocalS3ReplicaResources? localReplica,
        CancellationToken cancellationToken = default)
    {
        var bucket = await parameters.GetBucketAsync(cancellationToken);
        var path = await parameters.GetPathAsync(cancellationToken);
        var region = await parameters.GetRegionAsync(cancellationToken);
        var endpoint = await parameters.GetEndpointAsync(cancellationToken);

        litestream = builder.ExecutionContext.IsPublishMode
            ? litestream.WithS3Replica(
                bucket: bucket,
                path: path,
                region: region,
                endpointParameter: string.IsNullOrWhiteSpace(endpoint) ? null : parameters.Endpoint,
                accessKey: parameters.AccessKey,
                secretKey: parameters.SecretKey)
            : litestream.WithS3Replica(
                bucket: bucket,
                path: path,
                region: region,
                endpoint: localReplica?.Service.GetEndpoint("http"),
                accessKey: parameters.AccessKey,
                secretKey: parameters.SecretKey,
                forcePathStyle: true,
                skipVerify: false);

        if (localReplica is null)
        {
            return litestream;
        }

        return litestream
            .WaitFor(localReplica.Service)
            .WaitForCompletion(localReplica.BucketInitializer);
    }

    private static string BuildBucketInitializationScript(IReadOnlyCollection<string> bucketNames)
    {
        var createCommands = string.Join(
            " && ",
            bucketNames.Select(static bucketName => $"mc mb --ignore-existing local/{bucketName}"));

        return $$"""
            until mc alias set local {{LocalReplicaEndpoint}} "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD"; do
              sleep 1
            done
            {{createCommands}}
            """;
    }
}

internal sealed record LocalS3ReplicaResources(
    IResourceBuilder<ContainerResource> Service,
    IResourceBuilder<ContainerResource> BucketInitializer);
