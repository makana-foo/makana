using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Yarp;
using Makana.AppHost.Configuration;
using Makana.AppHost.Litestream;
using Makana.AppHost.PocketId;

namespace Makana.AppHost.Oidc;

internal static class OidcResourceBuilderExtensions
{
    private const string PocketIdEncryptionKeyParameterName = "pocketid-encryption-key";
    private const string PocketIdStaticApiKeyParameterName = "pocketid-static-api-key";
    private const string DexSessionCookieSecretParameterName = "dex-session-cookie-secret";
    private const string PocketIdDataDirectoryName = "store";
    private const string PocketIdDataMount = "/app/data";
    private const string PocketIdDatabasePath = $"{PocketIdDataMount}/{PocketIdDataDirectoryName}/pocket-id.db";
    private const string PocketIdDataVolume = "oidc-pocketid-data";
    private const string PocketIdLitestreamMount = "/var/lib/litestream/pocketid";

    private const string DexDataDirectoryName = "store";
    private const string DexDataMount = "/var/dex";
    private const string DexDatabasePath = $"{DexDataMount}/{DexDataDirectoryName}/dex.db";
    private const string DexDataVolume = "oidc-dex-data";
    private const string DexLitestreamMount = "/var/lib/litestream/dex";
    private const UnixFileMode SharedWritableDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
    public static IResourceBuilder<T> WithOidcEnvironment<T>(
        this IResourceBuilder<T> resource,
        OidcParameterResources parameters,
        OidcBuiltInEndpointReferences? builtInEndpoints = null,
        bool includeApiAudiences = false)
            where T : IResourceWithEnvironment
    {
        resource = resource
            .WithEnvironment("OIDC_PROVIDER", parameters.Provider)
            .WithEnvironment("OIDC_MODE", parameters.Mode)
            .WithEnvironment("OIDC_CLIENT_ID", parameters.ClientId);

        if (parameters.ClientSecret is not null)
        {
            resource = resource.WithEnvironment("OIDC_CLIENT_SECRET", parameters.ClientSecret);
        }

        resource = builtInEndpoints is null
            ? resource
                .WithEnvironment("OIDC_AUTHORITY", parameters.GetRequiredAuthority())
                .WithEnvironment("OIDC_METADATA_ADDRESS", parameters.MetadataAddress)
            : resource
                .WithEnvironment("OIDC_AUTHORITY", builtInEndpoints.Authority)
                .WithEnvironment("OIDC_METADATA_ADDRESS", builtInEndpoints.MetadataAddress)
                .WithEnvironment("OIDC_BACKCHANNEL_BASE_URL", builtInEndpoints.UpstreamAuthority);

        if (builtInEndpoints is null && parameters.UpstreamAuthority is not null)
        {
            resource = resource.WithEnvironment("OIDC_BACKCHANNEL_BASE_URL", parameters.GetRequiredUpstreamAuthority());
        }

        if (includeApiAudiences)
        {
            resource = resource
                .WithEnvironment("OIDC_API_AUDIENCE", parameters.ClientId)
                .WithEnvironment("OIDC_SUBJECT_TOKEN_AUDIENCE", parameters.ClientId);
        }

        return resource;
    }

    public static async Task<OidcBuiltInEndpointReferences?> AddOidcResourcesAsync(
        this IDistributedApplicationBuilder builder,
        OidcParameterResources parameters,
        PocketIdOptions pocketIdOptions,
        DexOptions dexOptions,
        LitestreamReplicaParameters litestreamParameters,
        LocalS3ReplicaResources? localS3,
        IResourceBuilder<YarpResource> publicIngress,
        PublicIngressOptions publicIngressOptions,
        CancellationToken cancellationToken = default)
    {
        var pocketIdEncryptionKey = builder.AddParameter(PocketIdEncryptionKeyParameterName, new GenerateParameterDefault(), secret: true, persist: true);
        var pocketIdStaticApiKey = builder.AddParameter(PocketIdStaticApiKeyParameterName, new GenerateParameterDefault(), secret: true, persist: true);
        var dexSessionCookieSecret = builder.AddParameter(DexSessionCookieSecretParameterName, new GenerateParameterDefault(), secret: true, persist: true);
        var usePocketId = await parameters.UsesBuiltInOidcAsync(cancellationToken);
        var useDex = await parameters.UsesDexAsync(cancellationToken);
        var pocketIdStorage = usePocketId ? builder.ResolvePocketIdStorage(pocketIdOptions) : null;
        var dexStorage = useDex ? builder.ResolveDexStorage(dexOptions) : null;
        var oidcRuntimeIds = ResolveRuntimeIds(pocketIdStorage, dexStorage);

        IResourceBuilder<ContainerResource>? pocketId = null;
        IResourceBuilder<DexResource>? dex = null;
        IResourceBuilder<LitestreamResource>? litestream = null;

        if (usePocketId)
        {
            pocketId = builder.AddContainer("oidc-pocketid", "ghcr.io/pocket-id/pocket-id", "v2")
                .WithHttpEndpoint(targetPort: 1411)
                .WithContainerRuntimeArgs("--user", oidcRuntimeIds.UserSpec)
                .WithEnvironment("TRUST_PROXY", "true")
                .WithEnvironment("ANALYTICS_DISABLED", "true")
                .WithEnvironment("ENCRYPTION_KEY", pocketIdEncryptionKey)
                .WithEnvironment("STATIC_API_KEY", pocketIdStaticApiKey)
                .WithEnvironment("PUID", oidcRuntimeIds.UserId.ToString())
                .WithEnvironment("PGID", oidcRuntimeIds.GroupId.ToString())
                .WithEnvironment("FILE_BACKEND", "database")
                .WithEnvironment("DB_CONNECTION_STRING", PocketIdDatabasePath)
                .WithHttpHealthCheck()
                .WithPocketIdDataStorage(pocketIdStorage!, oidcRuntimeIds)
                .WithEndpoint("http", endpoint => endpoint.IsExternal = false, createIfNotExists: false)
                .WithUrls(context => context.Urls.RemoveAll(static url => string.Equals(url.Endpoint?.EndpointName, "http", StringComparison.Ordinal)));

            if (string.Equals(builder.Environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            {
                pocketId = pocketId.WithEnvironment("DISABLE_RATE_LIMITING", "true");
            }
        }

        if (useDex)
        {
            var dexStaticClientSecret = await GetOptionalValueAsync(parameters.ClientSecret, cancellationToken);
            dex = builder.AddDex("oidc-dex")
                .WithHttpEndpoint(targetPort: 5556)
                .WithHttpEndpoint(name: "grpc", targetPort: 5557)
                .WithContainerRuntimeArgs("--user", oidcRuntimeIds.UserSpec)
                .WithEnvironment("DEX_SESSIONS_ENABLED", dexOptions.SessionsEnabled ? "true" : "false")
                .WithEnvironment("DEX_API_CONNECTORS_CRUD", "true")
                .WithHttpHealthCheck(path: "/.well-known/openid-configuration")
                .WithDexDataStorage(dexStorage!, oidcRuntimeIds)
                .WithEndpoint("http", endpoint => endpoint.IsExternal = false, createIfNotExists: false)
                .WithEndpoint("grpc", endpoint => endpoint.IsExternal = false, createIfNotExists: false)
                .WithUrls(context => context.Urls.RemoveAll(static url => string.Equals(url.Endpoint?.EndpointName, "http", StringComparison.Ordinal)));

            dex.Resource.StaticClientIsPublic = string.IsNullOrWhiteSpace(dexStaticClientSecret);

            if (pocketId is not null)
            {
                dex = dex.WaitFor(pocketId);
            }
        }

        if (usePocketId || useDex)
        {
            litestream = (await builder.AddLitestream("oidc-litestream")
                .WithConfiguredS3ReplicaAsync(builder, litestreamParameters, localS3, cancellationToken))
                .WithContainerUser(oidcRuntimeIds.UserId, oidcRuntimeIds.GroupId);

            if (usePocketId)
            {
                litestream = litestream.WithPocketIdLitestreamStorage(builder.ResolvePocketIdStorage(pocketIdOptions), oidcRuntimeIds);
                pocketId = pocketId?.WaitFor(litestream);
            }

            if (useDex)
            {
                litestream = litestream.WithDexLitestreamStorage(builder.ResolveDexStorage(dexOptions), oidcRuntimeIds);
                dex = dex?.WaitFor(litestream);
            }

            litestream = litestream.WithGeneratedConfigurationFile();

        }

        if (!usePocketId && !useDex)
        {
            return null;
        }

        var authority = publicIngress.GetEndpointUrlReference("https", KnownNetworkIdentifiers.LocalhostNetwork, publicIngressOptions.AuthHost);
        var upstreamAuthority = dex is not null
            ? dex.GetEndpointUrlReference("http")
            : pocketId?.GetEndpointUrlReference("http")
                ?? authority;
        var metadataAddress = ReferenceExpression.Create($"{upstreamAuthority}/.well-known/openid-configuration");

        if (pocketId is not null)
        {
            pocketId = pocketId.WithEnvironment("APP_URL", authority);
        }

        if (dex is not null && pocketId is not null)
        {
            dex = dex.WithDexConfiguration(
                parameters,
                pocketId,
                pocketIdStaticApiKey,
                oidcRuntimeIds,
                publicIngress,
                publicIngressOptions,
                dexOptions,
                dexSessionCookieSecret,
                await GetOptionalValueAsync(parameters.ClientSecret, cancellationToken));
        }

        return new OidcBuiltInEndpointReferences(
            authority,
            upstreamAuthority,
            metadataAddress,
            dex is not null
                ? dex
                : pocketId ?? throw new InvalidOperationException("Built-in OIDC requires an authority resource."),
            pocketId,
            dex);
    }

    private static IResourceBuilder<ContainerResource> WithPocketIdDataStorage(
        this IResourceBuilder<ContainerResource> resource,
        OidcStorageMount storage,
        ContainerUserIds runtimeIds)
    {
        resource = storage.BindMountPath is { } bindMountPath
            ? resource.WithBindMount(bindMountPath, PocketIdDataMount)
            : resource.WithVolume(storage.VolumeName!, PocketIdDataMount);

        return resource
            .WithContainerFiles(
                PocketIdDataMount,
                [CreateSharedWritableDirectory(PocketIdDataDirectoryName, runtimeIds)],
                runtimeIds.UserId,
                runtimeIds.GroupId);
    }

    private static IResourceBuilder<DexResource> WithDexDataStorage(
        this IResourceBuilder<DexResource> resource,
        OidcStorageMount storage,
        ContainerUserIds runtimeIds)
    {
        resource = storage.BindMountPath is { } bindMountPath
            ? resource.WithBindMount(bindMountPath, DexDataMount)
            : resource.WithVolume(storage.VolumeName!, DexDataMount);

        return resource
            .WithContainerFiles(
                DexDataMount,
                [CreateSharedWritableDirectory(DexDataDirectoryName, runtimeIds)],
                runtimeIds.UserId,
                runtimeIds.GroupId);
    }

    private static IResourceBuilder<LitestreamResource> WithPocketIdLitestreamStorage(
        this IResourceBuilder<LitestreamResource> resource,
        OidcStorageMount storage,
        ContainerUserIds runtimeIds)
    {
        resource = storage.BindMountPath is { } bindMountPath
            ? resource.WithDataBindMount(bindMountPath, PocketIdLitestreamMount)
            : resource.WithDataVolume(storage.VolumeName!, PocketIdLitestreamMount);

        return resource
            .WithContainerFiles(
                PocketIdLitestreamMount,
                [CreateSharedWritableDirectory(PocketIdDataDirectoryName, runtimeIds)],
                runtimeIds.UserId,
                runtimeIds.GroupId)
            .WithDatabase(
                $"{PocketIdLitestreamMount}/{PocketIdDataDirectoryName}/pocket-id.db",
                replicaPath: "pocket-id.db");
    }

    private static IResourceBuilder<LitestreamResource> WithDexLitestreamStorage(
        this IResourceBuilder<LitestreamResource> resource,
        OidcStorageMount storage,
        ContainerUserIds runtimeIds)
    {
        resource = storage.BindMountPath is { } bindMountPath
            ? resource.WithDataBindMount(bindMountPath, DexLitestreamMount)
            : resource.WithDataVolume(storage.VolumeName!, DexLitestreamMount);

        return resource
            .WithContainerFiles(
                DexLitestreamMount,
                [CreateSharedWritableDirectory(DexDataDirectoryName, runtimeIds)],
                runtimeIds.UserId,
                runtimeIds.GroupId)
            .WithDatabase(
                $"{DexLitestreamMount}/{DexDataDirectoryName}/dex.db",
                replicaPath: "dex/dex.db");
    }

    private static IResourceBuilder<DexResource> WithDexConfiguration(
        this IResourceBuilder<DexResource> dex,
        OidcParameterResources parameters,
        IResourceBuilder<ContainerResource> pocketId,
        IResourceBuilder<ParameterResource> pocketIdStaticApiKey,
        ContainerUserIds runtimeIds,
        IResourceBuilder<YarpResource> publicIngress,
        PublicIngressOptions publicIngressOptions,
        DexOptions dexOptions,
        IResourceBuilder<ParameterResource> dexSessionCookieSecret,
        string? dexStaticClientSecret)
    {
        return dex.WithContainerFiles(
            "/etc/dex",
            async (_, startCancellationToken) =>
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(pocketId.GetEndpoint("http", KnownNetworkIdentifiers.LocalhostNetwork).Url, UriKind.Absolute),
                    Timeout = TimeSpan.FromSeconds(5)
                };

                var publicAuthority = BuildPublicEndpointUri(
                    new Uri(publicIngress.GetEndpoint("https").Url, UriKind.Absolute),
                    publicIngressOptions.AuthHost);
                var upstreamInternalAuthority = new Uri(pocketId.GetEndpoint("http", KnownNetworkIdentifiers.DefaultAspireContainerNetwork).Url, UriKind.Absolute);
                var staticApiKey = await GetRequiredValueAsync(pocketIdStaticApiKey, startCancellationToken);
                var publicWebConsoleUrl = BuildPublicEndpointUri(
                    new Uri(publicIngress.GetEndpoint("https").Url, UriKind.Absolute),
                    publicIngressOptions.AppHost);
                var staticClientRedirectUri = new Uri(publicWebConsoleUrl, "/signin-oidc").ToString();
                var staticClientPostLogoutRedirectUri = new Uri(publicWebConsoleUrl, "/signout-callback-oidc").ToString();
                var sessions = dexOptions.SessionsEnabled
                    ? new DexSessionsConfiguration(
                        CookieName: dexOptions.SessionCookieName,
                        AbsoluteLifetime: dexOptions.SessionAbsoluteLifetime,
                        ValidIfNotUsedFor: dexOptions.SessionValidIfNotUsedFor,
                        RememberMeCheckedByDefault: dexOptions.SessionRememberMeCheckedByDefault,
                        CookieEncryptionKey: DeriveDexSessionCookieEncryptionKey(
                            await GetRequiredValueAsync(dexSessionCookieSecret, startCancellationToken)),
                        SsoSharedWithDefault: dexOptions.SessionSsoSharedWithDefault)
                    : null;

                var upstreamClientSecret = await PocketIdOidcClientConfigurator.EnsureAsync(
                    httpClient,
                    staticApiKey,
                    dexOptions.UpstreamClientId,
                    isPublic: false,
                    pkceEnabled: false,
                    publicWebConsoleUrl,
                    new Uri(publicAuthority, "/callback"),
                    new Uri(publicAuthority, "/logout/callback"),
                    startCancellationToken);

                var configuration = dex.Resource.RenderConfiguration(
                    new DexConfiguration(
                        Issuer: publicAuthority.ToString().TrimEnd('/'),
                        StoragePath: DexDatabasePath,
                        GrpcAddress: "0.0.0.0:5557",
                        Sessions: sessions,
                        StaticClientId: await GetRequiredValueAsync(parameters.ClientId, startCancellationToken),
                        StaticClientSecret: dexStaticClientSecret ?? string.Empty,
                        StaticClientRedirectUri: staticClientRedirectUri,
                        StaticClientPostLogoutRedirectUri: staticClientPostLogoutRedirectUri,
                        ConnectorId: dexOptions.ConnectorId,
                        ConnectorName: dexOptions.ConnectorName,
                        UpstreamIssuer: upstreamInternalAuthority.ToString().TrimEnd('/'),
                        UpstreamIssuerAlias: publicAuthority.ToString().TrimEnd('/'),
                        UpstreamClientId: dexOptions.UpstreamClientId,
                        UpstreamClientSecret: upstreamClientSecret,
                        UpstreamPkceChallenge: string.Empty,
                        UpstreamRedirectUri: new Uri(publicAuthority, "/callback").ToString(),
                        UpstreamAuthUrl: new Uri(publicAuthority, "/authorize").ToString(),
                        UpstreamTokenUrl: new Uri(upstreamInternalAuthority, "/api/oidc/token").ToString(),
                        UpstreamJwksUrl: new Uri(upstreamInternalAuthority, "/.well-known/jwks.json").ToString(),
                        UpstreamEndSessionUrl: new Uri(publicAuthority, "/api/oidc/end-session").ToString()));

                return
                [
                    new ContainerFile
                    {
                        Name = "config.yaml",
                        Contents = configuration,
                        Owner = runtimeIds.UserId,
                        Group = runtimeIds.GroupId,
                    }
                ];
            },
            runtimeIds.UserId,
            runtimeIds.GroupId);
    }

    private static OidcStorageMount ResolvePocketIdStorage(this IDistributedApplicationBuilder builder, PocketIdOptions options)
        => ResolveStorage(
            builder,
            options.UsePersistentState,
            options.ResolveDataDirectory(builder.Environment.ContentRootPath),
            options.VolumeName ?? PocketIdDataVolume);

    private static OidcStorageMount ResolveDexStorage(this IDistributedApplicationBuilder builder, DexOptions options)
        => ResolveStorage(
            builder,
            options.UsePersistentState,
            options.ResolveDataDirectory(builder.Environment.ContentRootPath),
            options.VolumeName ?? DexDataVolume);

    private static OidcStorageMount ResolveStorage(
        IDistributedApplicationBuilder builder,
        bool usePersistentState,
        string bindMountPath,
        string volumeName)
    {
        if (builder.ExecutionContext.IsPublishMode || usePersistentState)
        {
            return new OidcStorageMount(
                VolumeName: usePersistentState ? volumeName : $"{volumeName}-{Guid.NewGuid():N}",
                BindMountPath: null);
        }

        return new OidcStorageMount(VolumeName: null, BindMountPath: bindMountPath);
    }

    private static async Task<string> GetRequiredValueAsync(
        IResourceBuilder<ParameterResource> parameter,
        CancellationToken cancellationToken)
        => await parameter.Resource.GetValueAsync(cancellationToken)
            ?? throw new InvalidOperationException($"Parameter '{parameter.Resource.Name}' must have a value.");

    private static async Task<string?> GetOptionalValueAsync(
        IResourceBuilder<ParameterResource>? parameter,
        CancellationToken cancellationToken)
        => parameter is null
            ? null
            : await parameter.Resource.GetValueAsync(cancellationToken);

    private static string DeriveDexSessionCookieEncryptionKey(string secret)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(digest[..16]).ToLowerInvariant();
    }

    private static ContainerDirectory CreateSharedWritableDirectory(string name, ContainerUserIds runtimeIds)
        => new()
        {
            Name = name,
            Owner = runtimeIds.UserId,
            Group = runtimeIds.GroupId,
            Mode = SharedWritableDirectoryMode,
        };

    private static ContainerUserIds ResolveRuntimeIds(
        OidcStorageMount? pocketIdStorage,
        OidcStorageMount? dexStorage)
    {
        ContainerUserIds? runtimeIds = null;

        foreach (var storage in new[] { pocketIdStorage, dexStorage })
        {
            if (storage is null)
            {
                continue;
            }

            var storageRuntimeIds = ResolveRuntimeIds(storage);
            if (runtimeIds is null)
            {
                runtimeIds = storageRuntimeIds;
                continue;
            }

            if (runtimeIds.Value != storageRuntimeIds)
            {
                throw new InvalidOperationException(
                    "Pocket ID and Dex must use the same storage topology because the shared OIDC containers and Litestream sidecar require one consistent runtime identity. Configure both resources to use bind mounts or both to use named volumes.");
            }
        }

        return runtimeIds ?? AppHostContainerUsers.Oidc;
    }

    private static ContainerUserIds ResolveRuntimeIds(OidcStorageMount storage)
        => storage.BindMountPath is null
            ? AppHostContainerUsers.Oidc
            : AppHostContainerUsers.ResolveCurrentProcessOrDefault(AppHostContainerUsers.Oidc);

    private static Uri BuildPublicEndpointUri(Uri endpoint, string host)
        => new UriBuilder(endpoint)
        {
            Host = host
        }.Uri;

    private sealed record OidcStorageMount(string? VolumeName, string? BindMountPath);
}

internal sealed record OidcBuiltInEndpointReferences(
    ReferenceExpression Authority,
    ReferenceExpression UpstreamAuthority,
    ReferenceExpression MetadataAddress,
    IResourceBuilder<IResource> AuthorityResource,
    IResourceBuilder<ContainerResource>? PocketIdResource,
    IResourceBuilder<DexResource>? DexResource);
