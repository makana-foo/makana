using Aspire.Hosting.ApplicationModel;

namespace Makana.AppHost.Litestream;

internal static class LitestreamParameterResourceBuilderExtensions
{
    public static LitestreamReplicaParameters AddMakanaLitestreamParameters(this IDistributedApplicationBuilder builder)
        => builder.AddLitestreamParameters("makana");

    public static LitestreamReplicaParameters AddOidcLitestreamParameters(this IDistributedApplicationBuilder builder)
        => builder.AddLitestreamParameters("pocketid");

    private static LitestreamReplicaParameters AddLitestreamParameters(
        this IDistributedApplicationBuilder builder,
        string prefix)
    {
        return new LitestreamReplicaParameters(
            builder.AddParameter($"{prefix}-litestream-bucket"),
            builder.AddParameter($"{prefix}-litestream-path"),
            builder.AddParameter($"{prefix}-litestream-region"),
            builder.AddParameter($"{prefix}-litestream-endpoint"),
            builder.AddParameter($"{prefix}-litestream-access-key", secret: true),
            builder.AddParameter($"{prefix}-litestream-secret-key", secret: true));
    }
}

internal sealed record LitestreamReplicaParameters(
    IResourceBuilder<ParameterResource> Bucket,
    IResourceBuilder<ParameterResource> Path,
    IResourceBuilder<ParameterResource> Region,
    IResourceBuilder<ParameterResource> Endpoint,
    IResourceBuilder<ParameterResource> AccessKey,
    IResourceBuilder<ParameterResource> SecretKey)
{
    public async Task<string> GetBucketAsync(CancellationToken cancellationToken = default)
        => await GetRequiredValueAsync(Bucket.Resource, "bucket", cancellationToken);

    public async Task<string> GetPathAsync(CancellationToken cancellationToken = default)
        => await GetRequiredValueAsync(Path.Resource, "path", cancellationToken);

    public async Task<string> GetRegionAsync(CancellationToken cancellationToken = default)
        => await GetRequiredValueAsync(Region.Resource, "region", cancellationToken);

    public async Task<string> GetEndpointAsync(CancellationToken cancellationToken = default)
        => await GetRequiredValueAsync(Endpoint.Resource, "endpoint", cancellationToken);

    public async Task<string> GetAccessKeyAsync(CancellationToken cancellationToken = default)
        => await GetRequiredValueAsync(AccessKey.Resource, "access key", cancellationToken);

    public async Task<string> GetSecretKeyAsync(CancellationToken cancellationToken = default)
        => await GetRequiredValueAsync(SecretKey.Resource, "secret key", cancellationToken);

    private static async Task<string> GetRequiredValueAsync(
        ParameterResource resource,
        string name,
        CancellationToken cancellationToken)
    {
        return await resource.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"The Litestream replica {name} parameter must be configured.");
    }
}
