using Makana.AppHost;
using Makana.AppHost.Configuration;
using Makana.AppHost.PocketId;
using Aspire.Hosting.Yarp;
using Microsoft.Extensions.Configuration;
using System.Diagnostics;

var builder = DistributedApplication.CreateBuilder(args);
var applicationEnvironment = builder.Environment.EnvironmentName;
var testingWebConsoleApiToken = builder.Configuration["Testing:WebConsoleApiToken"];
var apiContainerOptions = builder.Configuration
    .GetRequiredSection(ApiContainerOptions.SectionName)
    .Get<ApiContainerOptions>() ?? new ApiContainerOptions();
var apiDataOptions = builder.Configuration
    .GetRequiredSection(ApiDataOptions.SectionName)
    .Get<ApiDataOptions>() ?? new ApiDataOptions();
var pocketIdOptions = builder.Configuration
    .GetSection(PocketIdOptions.SectionName)
    .Get<PocketIdOptions>() ?? new PocketIdOptions();
var dexOptions = builder.Configuration
    .GetSection(DexOptions.SectionName)
    .Get<DexOptions>() ?? new DexOptions();

apiContainerOptions.Validate();

const string OidcProviderParam = "oidc-provider";
const string OidcModeParam = "oidc-mode";
const string OidcAuthorityParam = "oidc-authority";
const string UpstreamOidcAuthorityParam = "upstream-oidc-authority";
const string OidcClientIdParam = "oidc-client-id";
const string OidcClientSecretParam = "oidc-client-secret";
const string AppHostParam = "app-host";
const string ApiHostParam = "api-host";
const string MakanaControlDatabaseRegistrationName = "makana-control";
const string MakanaApiDataRootRegistrationName = "makana-data-root";
const string MakanaLitestreamBucketParam = "makana-litestream-bucket";
const string MakanaLitestreamPathParam = "makana-litestream-path";
const string MakanaLitestreamRegionParam = "makana-litestream-region";
const string MakanaLitestreamEndpointParam = "makana-litestream-endpoint";
const string MakanaLitestreamAccessKeyParam = "makana-litestream-access-key";
const string MakanaLitestreamSecretKeyParam = "makana-litestream-secret-key";
const string PocketIdLitestreamBucketParam = "pocketid-litestream-bucket";
const string PocketIdLitestreamPathParam = "pocketid-litestream-path";
const string PocketIdLitestreamRegionParam = "pocketid-litestream-region";
const string PocketIdLitestreamEndpointParam = "pocketid-litestream-endpoint";
const string PocketIdLitestreamAccessKeyParam = "pocketid-litestream-access-key";
const string PocketIdLitestreamSecretKeyParam = "pocketid-litestream-secret-key";

var oidcProvider = builder.AddParameter(OidcProviderParam);
var oidcMode = builder.AddParameter(OidcModeParam);
var oidcAuthority = builder.AddParameter(OidcAuthorityParam);
var upstreamOidcAuthority = builder.AddParameter(
    UpstreamOidcAuthorityParam,
    builder.Configuration[$"Parameters:{UpstreamOidcAuthorityParam}"] ?? string.Empty);
var oidcClientId = builder.AddParameter(OidcClientIdParam, secret: true);
var appHost = builder.AddParameter(AppHostParam);
var apiHost = builder.AddParameter(ApiHostParam);
var oidcClientSecret = builder.AddParameter(
    OidcClientSecretParam,
    builder.Configuration[$"Parameters:{OidcClientSecretParam}"] ?? string.Empty,
    secret: true);
var oidcMetadataAddress = ReferenceExpression.Create($"{oidcAuthority}/.well-known/openid-configuration");

var makanaLitestreamBucket = builder.AddParameter(MakanaLitestreamBucketParam);
var makanaLitestreamPath = builder.AddParameter(MakanaLitestreamPathParam);
var makanaLitestreamRegion = builder.AddParameter(MakanaLitestreamRegionParam);
var makanaLitestreamEndpoint = builder.AddParameter(MakanaLitestreamEndpointParam);
var makanaLitestreamAccessKey = builder.AddParameter(MakanaLitestreamAccessKeyParam, secret: true);
var makanaLitestreamSecretKey = builder.AddParameter(MakanaLitestreamSecretKeyParam, secret: true);
var pocketIdLitestreamBucket = builder.AddParameter(PocketIdLitestreamBucketParam);
var pocketIdLitestreamPath = builder.AddParameter(PocketIdLitestreamPathParam);
var pocketIdLitestreamRegion = builder.AddParameter(PocketIdLitestreamRegionParam);
var pocketIdLitestreamEndpoint = builder.AddParameter(PocketIdLitestreamEndpointParam);
var pocketIdLitestreamAccessKey = builder.AddParameter(PocketIdLitestreamAccessKeyParam, secret: true);
var pocketIdLitestreamSecretKey = builder.AddParameter(PocketIdLitestreamSecretKeyParam, secret: true);

var makanaApiDbPath = builder.AddParameter("makana-api-db-path");

IResourceBuilder<ContainerResource>? localS3 = null;
if (!builder.ExecutionContext.IsPublishMode)
{
    var s3ProxyConfigPath = Path.Join(builder.Environment.ContentRootPath, "S3Proxy");
    localS3 = builder.AddContainer("s3", "andrewgaul/s3proxy")
        .WithHttpEndpoint(port: 3900, targetPort: 80)
        .WithVolume("s3proxy-data", "/data")
        .WithContainerFiles("/data", [new ContainerDirectory { Name = "makana-dev" }, new ContainerDirectory { Name = "makana-test" }])
        .WithContainerFiles("/etc/s3proxy", s3ProxyConfigPath)
        .WithArgs("java", "-jar", "/opt/s3proxy/s3proxy", "--properties", "/etc/s3proxy/s3proxy.conf");
}

const int AppUser = 1654;
const int AppGroup = 1654;
var appContainerUser = $"{AppUser}:{AppGroup}";
var hostMakanaDbPath = Path.GetFullPath(await makanaApiDbPath.Resource.GetValueAsync(default));
var hostMakanaDbDirectory = Path.GetDirectoryName(hostMakanaDbPath)
    ?? throw new InvalidOperationException("Unable to resolve the Makana API database directory.");
var localContainerUser = ResolveCurrentContainerUser();
var hostContainerUser = apiDataOptions.UsePersistentState ? null : localContainerUser;
var litestreamDataMount = "/var/lib/litestream";
var litestreamDataPath = $"{litestreamDataMount}/makana";

var apiDataMount = builder.ExecutionContext switch
{
    { IsPublishMode: true } => "/data",
    _ => hostMakanaDbDirectory
};

var apiDataPath = builder.ExecutionContext.IsPublishMode
    ? $"{apiDataMount}/makana"
    : apiDataMount;
var apiControlDatabasePath = builder.ExecutionContext.IsPublishMode
    ? $"{apiDataPath}/makana.db"
    : hostMakanaDbPath;

Directory.CreateDirectory(hostMakanaDbDirectory);
if (!apiDataOptions.UsePersistentState)
{
    EnsureWritableDirectory(hostMakanaDbDirectory);
}

var makanaLitestreamEndpointValue = await makanaLitestreamEndpoint.Resource.GetValueAsync(default);
var makanaLitestream = builder.AddLitestream("makana-api-litestream");
makanaLitestream = builder.ExecutionContext.IsPublishMode
    ? makanaLitestream.WithS3Replica(
        bucket: await makanaLitestreamBucket.Resource.GetValueAsync(default),
        path: await makanaLitestreamPath.Resource.GetValueAsync(default),
        region: await makanaLitestreamRegion.Resource.GetValueAsync(default),
        endpointParameter: string.IsNullOrWhiteSpace(makanaLitestreamEndpointValue) ? null : makanaLitestreamEndpoint,
        accessKey: makanaLitestreamAccessKey,
        secretKey: makanaLitestreamSecretKey)
    : makanaLitestream.WithS3Replica(
        bucket: await makanaLitestreamBucket.Resource.GetValueAsync(default),
        path: await makanaLitestreamPath.Resource.GetValueAsync(default),
        region: await makanaLitestreamRegion.Resource.GetValueAsync(default),
        endpoint: localS3?.GetEndpoint("http"),
        accessKey: makanaLitestreamAccessKey,
        secretKey: makanaLitestreamSecretKey,
        forcePathStyle: true,
        skipVerify: true);

if (localS3 is not null)
{
    makanaLitestream = makanaLitestream.WaitFor(localS3);
}

if (apiDataOptions.UsePersistentState)
{
    if (builder.ExecutionContext.IsPublishMode)
    {
        makanaLitestream = makanaLitestream
            .WithDataVolume("api-data");
    }
    else
    {
        makanaLitestream = makanaLitestream
            .WithDataBindMount(apiDataMount);
    }

    makanaLitestream = makanaLitestream
        .WithContainerFiles(litestreamDataMount, [new ContainerDirectory() {
            Name = "makana",
            Owner = AppUser,
            Group = AppGroup,
        }], AppUser, AppGroup)
        .WithDatabaseDirectory("makana", pattern: "*.db", recursive: true, watch: true);
}
else
{
    makanaLitestream = makanaLitestream
        .WithDataBindMount(hostMakanaDbDirectory, litestreamDataPath)
        .WithDatabaseDirectory(litestreamDataPath, pattern: "*.db", recursive: true, watch: true);

    if (!string.IsNullOrWhiteSpace(hostContainerUser))
    {
        makanaLitestream = makanaLitestream.WithContainerRuntimeArgs("--user", hostContainerUser);
    }
}

var api = builder.AddProject<Projects.Makana_Api>("api")
    .PublishAsDockerFile(resource =>
    {
        resource.WithDockerfile("api", "../", "Makana.Api/Dockerfile")
            .WithBuildArg("PUBLISH_AOT", apiContainerOptions.PublishAot.ToString().ToLowerInvariant())
            .WithBuildArg("FINAL_BASE_IMAGE", apiContainerOptions.ResolveFinalBaseImage())
            .WithHttpEndpoint(targetPort: 8080)
            .WithExternalHttpEndpoints();

        if (apiDataOptions.UsePersistentState)
        {
            resource = resource
                    .WithVolume("api-data", apiDataMount)
                    .WithContainerFiles(apiDataMount, [new ContainerDirectory() {
                        Name = "makana",
                        Owner = AppUser,
                        Group = AppGroup,
                    }], AppUser, AppGroup);
        }
        else
        {
            resource = resource.WithBindMount(hostMakanaDbDirectory, apiDataMount);

            if (!string.IsNullOrWhiteSpace(hostContainerUser))
            {
                resource = resource.WithContainerRuntimeArgs("--user", hostContainerUser);
            }
        }
    })
    .WaitFor(makanaLitestream)
    .WithEnvironment("Aspire__Litestream__Client__Database__" + MakanaControlDatabaseRegistrationName + "__DatabasePath", apiControlDatabasePath)
    .WithEnvironment("Aspire__Litestream__Client__Group__" + MakanaApiDataRootRegistrationName + "__DirectoryPath", apiDataPath)
    .WithEnvironment("Aspire__Litestream__Client__Group__" + MakanaApiDataRootRegistrationName + "__Pattern", "*.db")
    .WithEnvironment("Aspire__Litestream__Client__Group__" + MakanaApiDataRootRegistrationName + "__Recursive", "true")
    .WithEnvironment("Aspire__Litestream__Client__Group__" + MakanaApiDataRootRegistrationName + "__Watch", "true")
    .WithEnvironment("OIDC_PROVIDER", oidcProvider)
    .WithEnvironment("OIDC_MODE", oidcMode)
    .WithEnvironment("OIDC_AUTHORITY", oidcAuthority)
    .WithEnvironment("OIDC_CLIENT_ID", oidcClientId)
    .WithEnvironment("OIDC_CLIENT_SECRET", oidcClientSecret)
    .WithEnvironment("OIDC_API_AUDIENCE", oidcClientId)
    .WithEnvironment("OIDC_SUBJECT_TOKEN_AUDIENCE", oidcClientId)
    .WithEnvironment("OIDC_METADATA_ADDRESS", oidcMetadataAddress)
    .WithHttpHealthCheck(path: "/health");

if (!builder.ExecutionContext.IsPublishMode)
{
    api = api
        .WithEndpoint("https", endpoint =>
        {
            endpoint.Port = 55507;
            endpoint.IsProxied = false;
        })
        .WithEndpoint("http", endpoint =>
        {
            endpoint.Port = 55508;
            endpoint.IsProxied = false;
        })
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", applicationEnvironment)
        .WithEnvironment("Makana__Storage__ReplicaState", "absent");
}

if (makanaLitestream is not null)
{
    api.WaitFor(makanaLitestream);
}

var webConsole = builder.AddProject<Projects.Makana_WebConsole>("app")
    .WithExternalHttpEndpoints()
    .WithEnvironment("API_URL", api.GetEndpoint("http"))
    .WithEnvironment("OIDC_PROVIDER", oidcProvider)
    .WithEnvironment("OIDC_MODE", oidcMode)
    .WithEnvironment("OIDC_AUTHORITY", oidcAuthority)
    .WithEnvironment("OIDC_CLIENT_ID", oidcClientId)
    .WithEnvironment("OIDC_CLIENT_SECRET", oidcClientSecret)
    .WithEnvironment("OIDC_METADATA_ADDRESS", oidcMetadataAddress)
    .WithHttpHealthCheck(path: "/health");

if (!string.IsNullOrWhiteSpace(testingWebConsoleApiToken))
{
    webConsole = webConsole.WithEnvironment("MAKANA_WEBCONSOLE_API_TOKEN", testingWebConsoleApiToken);
}

if (!builder.ExecutionContext.IsPublishMode)
{
    webConsole = webConsole
        .WithEndpoint("https", endpoint =>
        {
            endpoint.Port = 7052;
            endpoint.IsProxied = false;
        })
        .WithEndpoint("http", endpoint =>
        {
            endpoint.Port = 5086;
            endpoint.IsProxied = false;
        });
}

const string LocalApiPublicHost = "api-makana.dev.localhost";
const string LocalWebConsolePublicHost = "app-makana.dev.localhost";

if (builder.ExecutionContext.IsPublishMode)
{
    webConsole = webConsole
        .WithEnvironment("Makana__ApiPublicUrl", ReferenceExpression.Create($"https://{apiHost}"))
        .WithEnvironment("Makana__WebConsolePublicUrl", ReferenceExpression.Create($"https://{appHost}"))
        .WithEnvironment("Makana__WebConsoleLoginUrl", ReferenceExpression.Create($"https://{appHost}"));
}
else
{
    webConsole = webConsole
        .WithEnvironmentUrlFromEndpoint("Makana__ApiPublicUrl", api, LocalApiPublicHost, "https")
        .WithEnvironmentUrlFromEndpoint("Makana__WebConsolePublicUrl", webConsole, LocalWebConsolePublicHost, "https")
        .WithEnvironmentUrlFromEndpoint("Makana__WebConsoleLoginUrl", webConsole, LocalWebConsolePublicHost, "https");
}

if (!builder.ExecutionContext.IsPublishMode)
{
    webConsole = webConsole.WithEnvironment("ASPNETCORE_ENVIRONMENT", applicationEnvironment);

    api = api.WithBrowserOriginsFromEndpoint(
        webConsole,
        configuredHosts: builder.Configuration.GetSection("Cors").GetSection("AllowedOrigins").Get<string[]?>());
}

/* Pocket ID */

const string PocketIdEncryptionKeyParam = "pocketid-encryption-key";
const string PocketIdStaticApiKeyParam = "pocketid-static-api-key";

var pocketIdEncryptionKey = builder.AddParameter(PocketIdEncryptionKeyParam, new GenerateParameterDefault(), secret: true, persist: true);
var pocketIdStaticApiKey = builder.AddParameter(PocketIdStaticApiKeyParam, new GenerateParameterDefault(), secret: true, persist: true);
var usePocketId = await oidcMode.Resource.GetValueAsync(default) is not "external"
    && await oidcProvider.Resource.GetValueAsync(default) is "pocketid" or "dex";
var useDex = await oidcMode.Resource.GetValueAsync(default) is not "external"
    && await oidcProvider.Resource.GetValueAsync(default) is "dex";
var pocketIdLitestreamEndpointValue = await pocketIdLitestreamEndpoint.Resource.GetValueAsync(default);
const string PocketIdDataDirectoryName = "store";
const string PocketIdStoragePath = $"/data/{PocketIdDataDirectoryName}/pocket-id.db";
const string PocketIdLitestreamMount = "/var/lib/litestream/pocketid";
const string DexDataDirectoryName = "store";
const string DexStoragePath = $"/var/dex/{DexDataDirectoryName}/dex.db";
const string DexLitestreamMount = "/var/lib/litestream/dex";
var oidcContainerUser = builder.ExecutionContext.IsPublishMode ? appContainerUser : localContainerUser;
var oidcRuntimeIds = SplitContainerUser(oidcContainerUser);
IResourceBuilder<ContainerResource>? pocketId = null;
IResourceBuilder<DexResource>? dex = null;
IResourceBuilder<LitestreamResource>? oidcLitestream = null;
string? pocketIdDataSource = null;
string? dexDataSource = null;
if (usePocketId)
{
    pocketId = builder.AddContainer("oidc-pocketid", "ghcr.io/pocket-id/pocket-id", "v2")
        .WithHttpEndpoint(port: 1411, targetPort: 1411)
        .WithEnvironment("TRUST_PROXY", "true")
        //.WithEnvironment("LOG_JSON", "true")
        .WithEnvironment("ANALYTICS_DISABLED", "true")
        .WithEnvironment("ENCRYPTION_KEY", pocketIdEncryptionKey)
        .WithEnvironment("STATIC_API_KEY", pocketIdStaticApiKey)
        .WithEnvironment("PUID", oidcRuntimeIds?.User ?? AppUser.ToString())
        .WithEnvironment("PGID", oidcRuntimeIds?.Group ?? AppGroup.ToString())
        .WithEnvironment("FILE_BACKEND", "database")
        .WithEnvironment("DB_CONNECTION_STRING", PocketIdStoragePath)
        .WithHttpHealthCheck();

    if (builder.ExecutionContext.IsPublishMode)
    {
        pocketId = pocketId
            .WithVolume("oidc-pocketid-data", "/data")
            .WithContainerFiles(
                "/data",
                [new ContainerDirectory { Name = PocketIdDataDirectoryName, Owner = AppUser, Group = AppGroup }],
                AppUser,
                AppGroup);
        pocketIdDataSource = "oidc-pocketid-data";
    }
    else
    {
        pocketIdDataSource = pocketIdOptions.ResolveDataDirectory(builder.Environment.ContentRootPath);

        EnsureWritableDirectory(pocketIdDataSource);
        EnsureWritableDirectory(Path.Combine(pocketIdDataSource, PocketIdDataDirectoryName));
        pocketId = pocketId.WithBindMount(pocketIdDataSource, "/data");
    }

    pocketId.WithEnvironment("APP_URL", useDex ? upstreamOidcAuthority : oidcAuthority);
}

if (useDex)
{
    var dexStaticClientSecretValue = await oidcClientSecret.Resource.GetValueAsync(default);
    dex = builder.AddDex("oidc-dex")
        .WithHttpEndpoint(port: 5556, targetPort: 5556)
        .WithHttpHealthCheck(path: "/.well-known/openid-configuration");

    dex.Resource.StaticClientIsPublic = string.IsNullOrWhiteSpace(dexStaticClientSecretValue);

    if (pocketId is not null)
    {
        dex = dex
            .WithEnvironment("DEX_UPSTREAM_TOKEN_URL", ReferenceExpression.Create($"{pocketId.GetEndpoint("http")}/api/oidc/token"))
            .WithEnvironment("DEX_UPSTREAM_JWKS_URL", ReferenceExpression.Create($"{pocketId.GetEndpoint("http")}/.well-known/jwks.json"))
            .WaitFor(pocketId);

        dex.OnBeforeResourceStarted(async (_, _, cancellationToken) =>
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(pocketId.GetEndpoint("http").Url, UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(5)
            };

            var dexIssuer = new Uri(await oidcAuthority.Resource.GetValueAsync(cancellationToken), UriKind.Absolute);
            var upstreamPublicAuthority = new Uri(await upstreamOidcAuthority.Resource.GetValueAsync(cancellationToken), UriKind.Absolute);
            var upstreamInternalPort = pocketId.GetEndpoint("http").TargetPort ?? pocketId.GetEndpoint("http").Port;
            var upstreamInternalAuthority = new Uri($"http://{pocketId.Resource.Name}.dev.internal:{upstreamInternalPort}", UriKind.Absolute);
            var staticApiKey = await pocketIdStaticApiKey.Resource.GetValueAsync(cancellationToken);
            var staticClientSecret = dexStaticClientSecretValue ?? string.Empty;
            var webConsoleHttpsEndpoint = new Uri(webConsole.GetEndpoint("https").Url, UriKind.Absolute);
            var staticClientRedirectUri = builder.ExecutionContext.IsPublishMode
                ? new Uri($"https://{appHost}/signin-oidc", UriKind.Absolute).ToString()
                : new Uri($"https://{LocalWebConsolePublicHost}:{webConsoleHttpsEndpoint.Port}/signin-oidc", UriKind.Absolute).ToString();

            var upstreamClientSecret = await PocketIdOidcClientConfigurator.EnsureAsync(
                httpClient,
                staticApiKey,
                dexOptions.UpstreamClientId,
                isPublic: false,
                launchUrl: dexIssuer,
                callbackUrl: new Uri(dexIssuer, "/callback"),
                logoutCallbackUrl: new Uri(dexIssuer, "/callback"),
                cancellationToken);

            string? dexConfigDirectory = Path.GetDirectoryName(dex.Resource.ConfigurationFileHostPath);
            if (dexConfigDirectory is null)
            {
                throw new InvalidOperationException($"Unable to determine a configuration directory for Dex resource '{dex.Resource.Name}'.");
            }

            Directory.CreateDirectory(dexConfigDirectory);
            File.WriteAllText(
                dex.Resource.ConfigurationFileHostPath,
                dex.Resource.RenderConfiguration(
                    new DexConfiguration(
                        Issuer: dexIssuer.ToString().TrimEnd('/'),
                        StoragePath: DexStoragePath,
                        StaticClientId: await oidcClientId.Resource.GetValueAsync(cancellationToken),
                        StaticClientSecret: staticClientSecret,
                        StaticClientRedirectUri: staticClientRedirectUri,
                        ConnectorId: dexOptions.ConnectorId,
                        ConnectorName: dexOptions.ConnectorName,
                        UpstreamIssuer: upstreamInternalAuthority.ToString().TrimEnd('/'),
                        UpstreamIssuerAlias: upstreamPublicAuthority.ToString().TrimEnd('/'),
                        UpstreamClientId: dexOptions.UpstreamClientId,
                        UpstreamClientSecret: upstreamClientSecret,
                        UpstreamPkceChallenge: "S256",
                        UpstreamRedirectUri: new Uri(dexIssuer, "/callback").ToString(),
                        UpstreamAuthUrl: new Uri(upstreamPublicAuthority, "/authorize").ToString(),
                        UpstreamTokenUrl: new Uri(upstreamInternalAuthority, "/api/oidc/token").ToString(),
                        UpstreamJwksUrl: new Uri(upstreamInternalAuthority, "/.well-known/jwks.json").ToString())));
        });
    }

    if (builder.ExecutionContext.IsPublishMode)
    {
        dex = dex
            .WithVolume("oidc-dex-data", "/var/dex")
            .WithContainerFiles(
                "/var/dex",
                [new ContainerDirectory { Name = DexDataDirectoryName, Owner = AppUser, Group = AppGroup }],
                AppUser,
                AppGroup);
        dexDataSource = "oidc-dex-data";
    }
    else
    {
        dexDataSource = dexOptions.ResolveDataDirectory(builder.Environment.ContentRootPath);
        EnsureWritableDirectory(dexDataSource);
        EnsureWritableDirectory(Path.Combine(dexDataSource, DexDataDirectoryName));

        dex = dex.WithBindMount(dexDataSource, "/var/dex");
    }

    if (!string.IsNullOrWhiteSpace(oidcContainerUser))
    {
        dex = dex.WithContainerRuntimeArgs("--user", oidcContainerUser);
    }

}

if (usePocketId || useDex)
{
    oidcLitestream = builder.AddLitestream("oidc-litestream");
    oidcLitestream = builder.ExecutionContext.IsPublishMode
        ? oidcLitestream.WithS3Replica(
            bucket: await pocketIdLitestreamBucket.Resource.GetValueAsync(default),
            path: await pocketIdLitestreamPath.Resource.GetValueAsync(default),
            region: await pocketIdLitestreamRegion.Resource.GetValueAsync(default),
            endpointParameter: string.IsNullOrWhiteSpace(pocketIdLitestreamEndpointValue) ? null : pocketIdLitestreamEndpoint,
            accessKey: pocketIdLitestreamAccessKey,
            secretKey: pocketIdLitestreamSecretKey)
        : oidcLitestream.WithS3Replica(
            bucket: await pocketIdLitestreamBucket.Resource.GetValueAsync(default),
            path: await pocketIdLitestreamPath.Resource.GetValueAsync(default),
            region: await pocketIdLitestreamRegion.Resource.GetValueAsync(default),
            endpoint: localS3?.GetEndpoint("http"),
            accessKey: pocketIdLitestreamAccessKey,
            secretKey: pocketIdLitestreamSecretKey,
            forcePathStyle: true,
            skipVerify: true);

    if (localS3 is not null)
    {
        oidcLitestream = oidcLitestream.WaitFor(localS3);
    }

    if (!string.IsNullOrWhiteSpace(oidcContainerUser))
    {
        oidcLitestream = oidcLitestream.WithContainerRuntimeArgs("--user", oidcContainerUser);
    }

    if (usePocketId)
    {
        oidcLitestream = builder.ExecutionContext.IsPublishMode
            ? oidcLitestream.WithDataVolume(pocketIdDataSource, PocketIdLitestreamMount)
            : oidcLitestream.WithDataBindMount(pocketIdDataSource!, PocketIdLitestreamMount);
        oidcLitestream = oidcLitestream.WithDatabase(
            $"{PocketIdLitestreamMount}/{PocketIdDataDirectoryName}/pocket-id.db",
            replicaPath: "pocket-id.db");
        pocketId = pocketId?.WaitFor(oidcLitestream);
    }

    if (useDex)
    {
        oidcLitestream = builder.ExecutionContext.IsPublishMode
            ? oidcLitestream.WithDataVolume(dexDataSource, DexLitestreamMount)
            : oidcLitestream.WithDataBindMount(dexDataSource!, DexLitestreamMount);
        oidcLitestream = oidcLitestream.WithDatabase(
            $"{DexLitestreamMount}/{DexDataDirectoryName}/dex.db",
            replicaPath: "dex/dex.db");
        dex = dex?.WaitFor(oidcLitestream);
    }
}

if (usePocketId || useDex)
{
    var oidcProxy = builder
        .AddYarp("oidc-proxy")
        .WithHttpsEndpoint(port: 3443, isProxied: false);

    var publicOidcAuthority = new Uri(await oidcAuthority.Resource.GetValueAsync(default), UriKind.Absolute);
    var publicUpstreamOidcAuthority = new Uri(await upstreamOidcAuthority.Resource.GetValueAsync(default), UriKind.Absolute);

    oidcProxy = oidcProxy.WithConfiguration(yarp =>
    {
        if (dex is not null)
        {
            yarp.AddRoute("/{**catch-all}", dex.GetEndpoint("http"))
                .WithMatchHosts(BuildAuthorityHostMatches(publicOidcAuthority, includeWildcardPort: false));
        }

        if (pocketId is not null)
        {
            yarp.AddRoute("/{**catch-all}", pocketId.GetEndpoint("http"))
                .WithMatchHosts(BuildAuthorityHostMatches(publicUpstreamOidcAuthority, includeWildcardPort: true));
        }
    });

    if (pocketId is not null)
    {
        oidcProxy = oidcProxy.WaitFor(pocketId);
    }

    if (dex is not null)
    {
        oidcProxy = oidcProxy.WaitFor(dex);
    }
}

/* End Pocket ID */

var app = builder.Build();
await app.RunAsync();

static void EnsureWritableDirectory(string path)
{
    Directory.CreateDirectory(path);

    if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
    {
        return;
    }

    File.SetUnixFileMode(
        path,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
}

static string[] BuildAuthorityHostMatches(Uri authority, bool includeWildcardPort)
{
    HashSet<string> matches = [authority.Host, authority.Authority];
    if (includeWildcardPort && !authority.IsDefaultPort)
    {
        matches.Add($"*:{authority.Port}");
    }

    return [.. matches];
}

static string? ResolveCurrentContainerUser()
{
    if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
    {
        return null;
    }

    var uid = RunIdCommand("-u");
    var gid = RunIdCommand("-g");

    return string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(gid)
        ? null
        : $"{uid}:{gid}";
}

static (string User, string Group)? SplitContainerUser(string? containerUser)
{
    if (string.IsNullOrWhiteSpace(containerUser))
    {
        return null;
    }

    var separatorIndex = containerUser.IndexOf(':');
    if (separatorIndex <= 0 || separatorIndex == containerUser.Length - 1)
    {
        return null;
    }

    return (containerUser[..separatorIndex], containerUser[(separatorIndex + 1)..]);
}

static string? RunIdCommand(string option)
{
    using var process = new Process
    {
        StartInfo = new ProcessStartInfo("id", option)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false
        }
    };

    if (!process.Start())
    {
        return null;
    }

    var output = process.StandardOutput.ReadToEnd().Trim();
    process.WaitForExit();

    return process.ExitCode == 0
        ? output
        : null;
}
