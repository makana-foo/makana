using Aspire.Hosting.ApplicationModel;

namespace Makana.AppHost.Litestream;

internal static class MakanaApiStorageBuilderExtensions
{
    internal const string MakanaApiDbPathParameterName = "makana-api-db-path";
    private const string ControlDatabaseRegistrationName = "makana-control";
    private const string ApiDataRootRegistrationName = "makana-data-root";
    private const string ApiDataVolumeName = "api-data";
    private const string DatabaseDirectoryName = "makana";
    private const string StackShardDirectoryName = "stacks";
    private const string LitestreamDataMount = "/var/lib/litestream";

    public static IResourceBuilder<ParameterResource> AddMakanaApiDbPathParameter(this IDistributedApplicationBuilder builder)
        => builder.AddParameter(MakanaApiDbPathParameterName);

    public static async Task<MakanaApiStorageContext> ResolveMakanaApiStorageContextAsync(
        this IDistributedApplicationBuilder builder,
        IResourceBuilder<ParameterResource> databasePathParameter,
        bool usePersistentState,
        CancellationToken cancellationToken = default)
    {
        var configuredDatabasePath = await databasePathParameter.Resource.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Parameter '{MakanaApiDbPathParameterName}' must have a value.");
        var controlDatabasePath = ResolveConfiguredPath(configuredDatabasePath, builder.Environment.ContentRootPath);
        var dataPath = Path.GetDirectoryName(controlDatabasePath)
            ?? throw new InvalidOperationException("Unable to resolve the Makana API data directory.");
        var litestreamDataPath = $"{LitestreamDataMount}/{DatabaseDirectoryName}";
        var usesContainerStorage = builder.ExecutionContext.IsPublishMode;

        return new MakanaApiStorageContext(
            usesContainerStorage ? (usePersistentState ? ApiDataVolumeName : $"{ApiDataVolumeName}-{Guid.NewGuid():N}") : null,
            usesContainerStorage ? null : dataPath,
            dataPath,
            controlDatabasePath,
            Path.Join(dataPath, StackShardDirectoryName),
            litestreamDataPath);
    }

    public static async Task<IResourceBuilder<LitestreamResource>> AddMakanaApiLitestreamAsync(
        this IDistributedApplicationBuilder builder,
        MakanaApiStorageContext storage,
        LitestreamReplicaParameters replicaParameters,
        LocalS3ReplicaResources? localReplica,
        CancellationToken cancellationToken = default)
    {
        var runtimeUser = storage.BindMountPath is null
            ? AppHostContainerUsers.Api
            : AppHostContainerUsers.ResolveCurrentProcessOrDefault(AppHostContainerUsers.Api);

        var resource = (await builder.AddLitestream("makana-api-litestream")
                .WithConfiguredS3ReplicaAsync(builder, replicaParameters, localReplica, cancellationToken))
            .WithContainerUser(runtimeUser.UserId, runtimeUser.GroupId);

        resource = storage.BindMountPath is { } bindMountPath
            ? resource.WithDataBindMount(bindMountPath, storage.LitestreamDataPath)
            : resource.WithDataVolume(storage.VolumeName!, storage.LitestreamDataPath);

        return resource
            .WithContainerFiles(
                storage.LitestreamDataPath,
                [
                    new ContainerDirectory
                    {
                        Name = StackShardDirectoryName,
                        Owner = runtimeUser.UserId,
                        Group = runtimeUser.GroupId,
                    }
                ],
                runtimeUser.UserId,
                runtimeUser.GroupId)
            .WithDatabaseDirectory(storage.LitestreamDataPath, pattern: "*.db", recursive: true, watch: true)
            .WithGeneratedConfigurationFile();
    }

    public static IResourceBuilder<T> WithMakanaApiLitestreamEnvironment<T>(
        this IResourceBuilder<T> resource,
        MakanaApiStorageContext storage)
            where T : IResourceWithEnvironment
    {
        return resource
            .WithEnvironment($"Aspire__Litestream__Client__Database__{ControlDatabaseRegistrationName}__DatabasePath", storage.ControlDatabasePath)
            .WithEnvironment($"Aspire__Litestream__Client__Group__{ApiDataRootRegistrationName}__DirectoryPath", storage.DataPath)
            .WithEnvironment($"Aspire__Litestream__Client__Group__{ApiDataRootRegistrationName}__Pattern", "*.db")
            .WithEnvironment($"Aspire__Litestream__Client__Group__{ApiDataRootRegistrationName}__Recursive", "true")
            .WithEnvironment($"Aspire__Litestream__Client__Group__{ApiDataRootRegistrationName}__Watch", "true");
    }

    private static string ResolveConfiguredPath(string configuredPath, string contentRootPath)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return Path.GetFullPath(configuredPath);
        }

        return Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
    }
}

internal sealed record MakanaApiStorageContext(
    string? VolumeName,
    string? BindMountPath,
    string DataPath,
    string ControlDatabasePath,
    string ShardDirectoryPath,
    string LitestreamDataPath);
