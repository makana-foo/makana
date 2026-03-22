var builder = DistributedApplication.CreateBuilder(args);

const string OidcProviderParam = "oidc-provider";
const string OidcModeParam = "oidc-mode";
const string OidcAuthorityParam = "oidc-authority";
const string OidcClientIdParam = "oidc-client-id";
const string OidcClientSecretParam = "oidc-client-secret";
const string PocketIdEncryptionKeyParam = "pocketid-encryption-key";

var oidcProvider = builder.AddParameter(OidcProviderParam);
var oidcMode = builder.AddParameter(OidcModeParam);
var oidcAuthority = builder.AddParameter(OidcAuthorityParam);
var oidcClientId = builder.AddParameter(OidcClientIdParam, secret: true);
var oidcClientSecret = builder.AddParameter(OidcClientSecretParam, secret: true);
var pocketIdEncryptionKey = builder.AddParameter(
    PocketIdEncryptionKeyParam,
    secret: true);

var makanaApiDbPath = builder.AddParameter("makana-api-db-path");

var oidcProviderRaw = await oidcProvider.Resource.GetValueAsync(CancellationToken.None);
var oidcModeRaw = await oidcMode.Resource.GetValueAsync(CancellationToken.None);
var oidcProviderValue = oidcProviderRaw!;
var oidcModeValue = oidcModeRaw!;
var oidcMetadataAddress = ReferenceExpression.Create($"{oidcAuthority}/.well-known/openid-configuration");

var api = builder.AddProject<Projects.Makana_Api>("api")
    .WithEnvironment("DOTNET_API_DB", makanaApiDbPath)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("OIDC_PROVIDER", oidcProvider)
    .WithEnvironment("OIDC_MODE", oidcMode)
    .WithEnvironment("OIDC_CLIENT_ID", oidcClientId)
    .WithEnvironment("OIDC_CLIENT_SECRET", oidcClientSecret)
    .WithEnvironment("OIDC_API_AUDIENCE", oidcClientId)
    .WithEnvironment("OIDC_SUBJECT_TOKEN_AUDIENCE", oidcClientId)
    .WithEnvironment("OIDC_METADATA_ADDRESS", oidcMetadataAddress)
    .WithHttpHealthCheck(path: "/health", endpointName: "http");

var webConsole = builder.AddProject<Projects.Makana_WebConsole>("app")
    .WithReference(api)
    .WithEnvironment("PULUMI_API", api.GetEndpoint("https"))
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("DOTNET_ENVIRONMENT", builder.Environment.EnvironmentName)
    .WithEnvironment("OIDC_PROVIDER", oidcProvider)
    .WithEnvironment("OIDC_MODE", oidcMode)
    .WithEnvironment("OIDC_CLIENT_ID", oidcClientId)
    .WithEnvironment("OIDC_CLIENT_SECRET", oidcClientSecret)
    .WithEnvironment("OIDC_METADATA_ADDRESS", oidcMetadataAddress)
    .WithHttpHealthCheck(path: "/health", endpointName: "http")
    .WaitFor(api);

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
    if (useLocal)
    {
        var casdoorDataDir = Path.Combine(builder.Environment.ContentRootPath, ".data", "casdoor");
        Directory.CreateDirectory(casdoorDataDir);

        var casdoor = builder
            .AddContainer("oidc-casdoor", "casbin/casdoor", "latest")
            .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")
            .WithBindMount(casdoorDataDir, "/data")
            .WithEnvironment("RUNNING_IN_DOCKER", "true")
            .WithEnvironment("driverName", "sqlite")
            .WithEnvironment("dataSourceName", "file:/data/casdoor.db?cache=shared")
            .WithEnvironment("dbName", "casdoor")
            .WithHttpHealthCheck(path: "/.well-known/openid-configuration", endpointName: "http");

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
    if (useLocal)
    {
        var pocketIdDataDir = Path.Combine(builder.Environment.ContentRootPath, ".data", "pocketid");
        Directory.CreateDirectory(pocketIdDataDir);

        var pocketId = builder
            .AddContainer("oidc-pocketid", "ghcr.io/pocket-id/pocket-id", "v2")
            .WithHttpEndpoint(port: 1411, targetPort: 1411, name: "http")
            .WithBindMount(pocketIdDataDir, "/app/data")
            .WithEnvironment("TRUST_PROXY", "true")
            .WithEnvironment("ENCRYPTION_KEY", pocketIdEncryptionKey)
            .WithHttpHealthCheck(path: "/", endpointName: "http");

        var pocketIdProxy = builder
            .AddYarp("oidc-pocketid-proxy")
            .WithHttpsEndpoint(port: 3443, name: "https")
            .WithConfiguration(yarp =>
            {
                yarp.AddRoute(pocketId.GetEndpoint("http"));
            })
            .WithHttpHealthCheck(path: "/.well-known/openid-configuration", endpointName: "http")
            .WaitFor(pocketId);

        var publicAuthority = oidcAuthority;
        var metadataEndpoint = pocketIdProxy.GetEndpoint("http");
        var localMetadataAddress = ReferenceExpression.Create($"{metadataEndpoint}/.well-known/openid-configuration");

        pocketId.WithEnvironment("APP_URL", publicAuthority);

        api.WithEnvironment("OIDC_AUTHORITY", publicAuthority)
           .WithEnvironment("OIDC_METADATA_ADDRESS", localMetadataAddress);

        webConsole.WithEnvironment("OIDC_AUTHORITY", publicAuthority)
                  .WithEnvironment("OIDC_METADATA_ADDRESS", localMetadataAddress);

        return;
    }

    api.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
    webConsole.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
}
