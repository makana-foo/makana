using Makana.AppHost;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;
using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var acaEnvironment = builder.AddAzureContainerAppEnvironment("aca")
    .WithAzdResourceNaming()
    .WithDashboard(true);

const string OidcProviderParam = "oidc-provider";
const string OidcModeParam = "oidc-mode";
const string OidcAuthorityParam = "oidc-authority";
const string OidcClientIdParam = "oidc-client-id";
const string OidcClientSecretParam = "oidc-client-secret";
const string PocketIdEncryptionKeyParam = "pocketid-encryption-key";
const string PocketIdStaticApiKeyParam = "pocketid-static-api-key";
const string AppHostParam = "app-host";
const string ApiHostParam = "api-host";
const string AuthHostParam = "auth-host";
const string AppCertNameParam = "app-cert-name";
const string ApiCertNameParam = "api-cert-name";
const string AuthCertNameParam = "auth-cert-name";
const string PocketIdPublishDataVolumeName = "pocketid-data";
const string PocketIdPublishDataMountPath = "/mnt/pocketid";
var pocketIdBootstrapContextPath = Path.Combine(builder.Environment.ContentRootPath, "PocketId");
const string NonProdLaunchProfileName = "NonProd";
const string DefaultLaunchProfileName = "https";
const string TestingLaunchProfileName = "Testing";

var oidcProvider = builder.AddParameter(OidcProviderParam);
var oidcMode = builder.AddParameter(OidcModeParam);
var oidcAuthority = builder.AddParameter(OidcAuthorityParam);
var oidcClientId = builder.AddParameter(OidcClientIdParam, secret: true);
var oidcClientSecret = builder.AddParameter(OidcClientSecretParam, secret: true);
var pocketIdEncryptionKey = builder.AddParameter(
    PocketIdEncryptionKeyParam,
    secret: true);
var pocketIdStaticApiKey = builder.AddParameter(
    PocketIdStaticApiKeyParam,
    secret: true);

var makanaApiDbPath = builder.AddParameter("makana-api-db-path");
var runLaunchProfileName = string.Equals(builder.Environment.EnvironmentName, "Testing", StringComparison.Ordinal)
    ? TestingLaunchProfileName
    : DefaultLaunchProfileName;
var usePersistentPocketIdState = builder.Configuration.GetValue("PocketId:UsePersistentState", true);

var oidcProviderRaw = await oidcProvider.Resource.GetValueAsync(CancellationToken.None);
var oidcModeRaw = await oidcMode.Resource.GetValueAsync(CancellationToken.None);
var oidcProviderValue = NormalizeParameterValue(oidcProviderRaw, "pocketid");
var oidcModeValue = NormalizeParameterValue(oidcModeRaw, "local");
var oidcMetadataAddress = ReferenceExpression.Create($"{oidcAuthority}/.well-known/openid-configuration");

var api = builder.AddProject<Projects.Makana_Api>(
        "api",
        launchProfileName: builder.ExecutionContext.IsPublishMode ? NonProdLaunchProfileName : runLaunchProfileName)
    .WithExternalHttpEndpoints()
    .WithEnvironment("DOTNET_API_DB", makanaApiDbPath)
    .WithEnvironment("OIDC_PROVIDER", oidcProvider)
    .WithEnvironment("OIDC_MODE", oidcMode)
    .WithEnvironment("OIDC_CLIENT_ID", oidcClientId)
    .WithEnvironment("OIDC_CLIENT_SECRET", oidcClientSecret)
    .WithEnvironment("OIDC_API_AUDIENCE", oidcClientId)
    .WithEnvironment("OIDC_SUBJECT_TOKEN_AUDIENCE", oidcClientId)
    .WithEnvironment("OIDC_METADATA_ADDRESS", oidcMetadataAddress)
    .WithHttpHealthCheck(path: "/health");

var webConsole = builder.AddProject<Projects.Makana_WebConsole>(
        "app",
        launchProfileName: builder.ExecutionContext.IsPublishMode ? NonProdLaunchProfileName : runLaunchProfileName)
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WithEnvironment("OIDC_PROVIDER", oidcProvider)
    .WithEnvironment("OIDC_MODE", oidcMode)
    .WithEnvironment("OIDC_CLIENT_ID", oidcClientId)
    .WithEnvironment("OIDC_CLIENT_SECRET", oidcClientSecret)
    .WithEnvironment("OIDC_METADATA_ADDRESS", oidcMetadataAddress)
    .WithHttpHealthCheck(path: "/health");

webConsole = webConsole
    .WithEnvironmentUrlFromEndpoint("Makana__ApiPublicUrl", api)
    .WithEnvironmentUrlFromEndpoint("Makana__WebConsolePublicUrl", webConsole);

if (!builder.ExecutionContext.IsPublishMode)
{
    api = api.WithBrowserOriginsFromEndpoint(
        webConsole,
        configuredHosts: builder.Configuration.GetSection("Cors").GetSection("WebConsoleOriginHosts").Get<string[]?>());
}

if (builder.ExecutionContext.IsPublishMode)
{
    ConfigureAcaPublishing();
}

var useLocalProvider = oidcModeValue != "external";

switch (oidcProviderValue)
{
    case "casdoor":
        ConfigureCasdoorProvider(useLocalProvider);
        break;
    case "pocketid":
        ConfigurePocketIdProvider(useLocalProvider);
        break;
    default:
        throw new InvalidOperationException($"Unsupported OIDC provider '{oidcProviderValue}'. Supported values: casdoor, pocketid.");
}

var app = builder.Build();
await app.RunAsync();

void ConfigureCasdoorProvider(bool useLocal)
{
    if (builder.ExecutionContext.IsPublishMode && useLocal)
    {
        var authHost = builder.AddParameter(AuthHostParam);
        var authCertName = builder.AddParameter(AuthCertNameParam);
        builder
            .AddContainer("oidc-casdoor", "casbin/casdoor", "latest")
            .WithHttpEndpoint(targetPort: 8000)
            .WithEnvironment("RUNNING_IN_DOCKER", "true")
            .WithEnvironment("driverName", "sqlite")
            .WithEnvironment("dataSourceName", "file:/data/casdoor.db?cache=shared")
            .WithEnvironment("dbName", "casdoor")
            .PublishAsAzureContainerApp((infrastructure, app) =>
            {
                app.ConfigureCustomDomain(authHost, authCertName);
            });

        api.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
        webConsole.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
        return;
    }

    if (useLocal)
    {
        var casdoorDataDir = Path.Combine(builder.Environment.ContentRootPath, ".data", "casdoor");
        Directory.CreateDirectory(casdoorDataDir);

        var casdoor = builder
            .AddContainer("oidc-casdoor", "casbin/casdoor", "latest")
            .WithHttpEndpoint(port: 8000, targetPort: 8000)
            .WithBindMount(casdoorDataDir, "/data")
            .WithEnvironment("RUNNING_IN_DOCKER", "true")
            .WithEnvironment("driverName", "sqlite")
            .WithEnvironment("dataSourceName", "file:/data/casdoor.db?cache=shared")
            .WithEnvironment("dbName", "casdoor")
            .WithHttpHealthCheck(path: "/.well-known/openid-configuration");

        var localAuthority = casdoor.GetEndpoint("http");
        var localMetadataAddress = ReferenceExpression.Create($"{localAuthority}/.well-known/openid-configuration");

        casdoor.WithEnvironment("origin", localAuthority);
        casdoor.WithEnvironment("originFrontend", localAuthority);

        api.WithEnvironment("OIDC_AUTHORITY", localAuthority)
           .WithEnvironment("OIDC_METADATA_ADDRESS", localMetadataAddress)
           .WaitFor(casdoor);

        webConsole.WithEnvironment("OIDC_AUTHORITY", localAuthority)
                  .WithEnvironment("OIDC_METADATA_ADDRESS", localMetadataAddress)
                  .WaitFor(casdoor);

        return;
    }

    api.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
    webConsole.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
}

void ConfigurePocketIdProvider(bool useLocal)
{
    if (!useLocal)
    {
        api.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
        webConsole.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);

        return;
    }

    if (builder.ExecutionContext.IsPublishMode)
    {
        var authHost = builder.AddParameter(AuthHostParam);
        var authCertName = builder.AddParameter(AuthCertNameParam);
        builder
            .AddContainer("oidc-pocketid", "makana-pocketid")
            .WithDockerfile(pocketIdBootstrapContextPath)
            .WithHttpEndpoint(targetPort: 1411)
            .WithExternalHttpEndpoints()
            .WithEnvironment("TRUST_PROXY", "true")
            .WithEnvironment("ENCRYPTION_KEY", pocketIdEncryptionKey)
            .WithEnvironment("STATIC_API_KEY", pocketIdStaticApiKey)
            .WithEnvironment("APP_URL", oidcAuthority)
            .WithEnvironment("DB_CONNECTION_STRING", $"{PocketIdPublishDataMountPath}/pocket-id.db")
            .WithEnvironment("FILE_BACKEND", "database")
            .WithHttpHealthCheck(path: "/.well-known/openid-configuration")
            .PublishAsAzureContainerApp((infrastructure, app) =>
            {
                app.Template ??= new ContainerAppTemplate();
                app.Template.Scale ??= new ContainerAppScale();
                app.Template.Scale.MinReplicas = 1;
                app.Template.Scale.MaxReplicas = 1;
                ConfigurePocketIdPublishBootstrap(app.Template);
                app.ConfigureCustomDomain(authHost, authCertName);
            });

        api.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
        webConsole.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
        return;
    }

    var configuredPocketIdDataDir = builder.Configuration["PocketId:DataDirectory"];
    var pocketIdDataDir = !string.IsNullOrWhiteSpace(configuredPocketIdDataDir)
        ? configuredPocketIdDataDir
        : usePersistentPocketIdState
            ? Path.Combine(builder.Environment.ContentRootPath, ".data", "pocketid")
            : Path.Combine(Path.GetTempPath(), "makana-pocketid", Guid.NewGuid().ToString("N"));
    var pocketIdLifetime = usePersistentPocketIdState
        ? ContainerLifetime.Persistent
        : ContainerLifetime.Session;
    Directory.CreateDirectory(pocketIdDataDir);

    var pocketId = builder
        .AddContainer("oidc-pocketid", "makana-pocketid")
        .WithDockerfile(pocketIdBootstrapContextPath)
        .WithLifetime(pocketIdLifetime)
        .WithHttpEndpoint(port: 1411, targetPort: 1411)
        .WithBindMount(pocketIdDataDir, "/app/data")
        .WithEntrypoint("/bin/sh")
        .WithArgs("-c", "/bootstrap/bootstrap-pocketid.sh && exec /app/docker/entrypoint.sh /app/pocket-id")
        .WithEnvironment("TRUST_PROXY", "true")
        .WithEnvironment("ENCRYPTION_KEY", pocketIdEncryptionKey)
        .WithEnvironment("STATIC_API_KEY", pocketIdStaticApiKey)
        .WithEnvironment("DB_CONNECTION_STRING", "/app/data/pocket-id.db")
        .WithEnvironment("FILE_BACKEND", "database")
        .WithHttpHealthCheck();

    var pocketIdProxy = builder
        .AddYarp("oidc-pocketid-proxy")
        .WithLifetime(pocketIdLifetime)
        .WithHttpsEndpoint(port: 3443, name: "https", isProxied: false)
        .WithConfiguration(yarp =>
        {
            yarp.AddRoute(pocketId.GetEndpoint("http"));
        })
        .WithHttpHealthCheck(path: "/.well-known/openid-configuration")
        .WaitFor(pocketId);

    var publicAuthority = oidcAuthority;
    var metadataEndpoint = pocketIdProxy.GetEndpoint("http");
    var localMetadataAddress = ReferenceExpression.Create($"{metadataEndpoint}/.well-known/openid-configuration");

    pocketId.WithEnvironment("APP_URL", publicAuthority);

    api.WithEnvironment("OIDC_AUTHORITY", publicAuthority)
       .WithEnvironment("OIDC_METADATA_ADDRESS", localMetadataAddress);

    webConsole.WithEnvironment("OIDC_AUTHORITY", publicAuthority)
              .WithEnvironment("OIDC_METADATA_ADDRESS", localMetadataAddress);
}

void ConfigureAcaPublishing()
{
    var appHost = builder.AddParameter(AppHostParam);
    var apiHost = builder.AddParameter(ApiHostParam);
    var appCertName = builder.AddParameter(AppCertNameParam);
    var apiCertName = builder.AddParameter(ApiCertNameParam);
    webConsole.WithEnvironment("API_HTTPS", ReferenceExpression.Create($"https://{apiHost}"));
    webConsole.WithEnvironment("Makana__ApiPublicUrl", ReferenceExpression.Create($"https://{apiHost}"));
    webConsole.WithEnvironment("Makana__WebConsolePublicUrl", ReferenceExpression.Create($"https://{appHost}"));
    api.WithEnvironment("Cors__AllowedOrigins__0", ReferenceExpression.Create($"https://{appHost}"));

    api.PublishAsAzureContainerApp((infrastructure, app) =>
    {
        app.ConfigureCustomDomain(apiHost, apiCertName);
    });

    webConsole.PublishAsAzureContainerApp((infrastructure, app) =>
    {
        app.ConfigureCustomDomain(appHost, appCertName);
    });
}

void ConfigurePocketIdPublishBootstrap(ContainerAppTemplate template)
{
    EnsureVolume(
        template,
        PocketIdPublishDataVolumeName,
        ContainerAppStorageType.EmptyDir);

    var mainContainer = template.Containers.Single().Unwrap();

    EnsureVolumeMount(
        mainContainer.VolumeMounts,
        PocketIdPublishDataVolumeName,
        PocketIdPublishDataMountPath);

    if (template.InitContainers.Any(container => container.Unwrap().Name.Value == "bootstrap-pocketid-client"))
    {
        return;
    }

    template.InitContainers.Add(CreatePocketIdBootstrapInitContainer(mainContainer));
}

ContainerAppInitContainer CreatePocketIdBootstrapInitContainer(ContainerAppContainer mainContainer)
{
    var initContainer = new ContainerAppInitContainer
    {
        Name = "bootstrap-pocketid-client",
        Image = mainContainer.Image,
    };

    initContainer.Command.Add("/bootstrap/bootstrap-pocketid.sh");

    initContainer.Env.Add(new ContainerAppEnvironmentVariable
    {
        Name = "ENCRYPTION_KEY",
        SecretRef = GetRequiredEnvironment(mainContainer, "ENCRYPTION_KEY").SecretRef,
    });
    initContainer.Env.Add(new ContainerAppEnvironmentVariable
    {
        Name = "DB_CONNECTION_STRING",
        Value = $"{PocketIdPublishDataMountPath}/pocket-id.db",
    });
    initContainer.Env.Add(new ContainerAppEnvironmentVariable
    {
        Name = "FILE_BACKEND",
        Value = "database",
    });

    EnsureVolumeMount(
        initContainer.VolumeMounts,
        PocketIdPublishDataVolumeName,
        PocketIdPublishDataMountPath);

    return initContainer;
}

static ContainerAppEnvironmentVariable GetRequiredEnvironment(ContainerAppContainer container, string name)
{
    return container.Env.Single(env => env.Unwrap().Name.Value == name).Unwrap();
}

static void EnsureVolume(
    ContainerAppTemplate template,
    string name,
    ContainerAppStorageType storageType)
{
    if (template.Volumes.Any(volume => volume.Unwrap().Name.Value == name))
    {
        return;
    }

    template.Volumes.Add(new ContainerAppVolume
    {
        Name = name,
        StorageType = storageType,
    });
}

static void EnsureVolumeMount(
    Azure.Provisioning.BicepList<ContainerAppVolumeMount> mounts,
    string volumeName,
    string mountPath)
{
    if (mounts.Any(mount => mount.Unwrap().VolumeName.Value == volumeName && mount.Unwrap().MountPath.Value == mountPath))
    {
        return;
    }

    mounts.Add(new ContainerAppVolumeMount
    {
        VolumeName = volumeName,
        MountPath = mountPath,
    });
}
static string NormalizeParameterValue(string? value, string fallback)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return fallback;
    }

    return value.Trim().ToLowerInvariant();
}
