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

var makanaApiTestToken = builder.AddParameter("makana-api-test-token", secret: true);
var makanaBootstrapUserId = builder.AddParameter("makana-bootstrap-user-id");
var makanaBootstrapUserLogin = builder.AddParameter("makana-bootstrap-user-login");
var makanaApiDbPath = builder.AddParameter("makana-api-db-path");

var oidcProviderRaw = await oidcProvider.Resource.GetValueAsync(CancellationToken.None);
var oidcModeRaw = await oidcMode.Resource.GetValueAsync(CancellationToken.None);
var oidcProviderValue = oidcProviderRaw!;
var oidcModeValue = oidcModeRaw!;
var oidcMetadataAddress = ReferenceExpression.Create($"{oidcAuthority}/.well-known/openid-configuration");

var api = builder.AddProject<Projects.Makana_Api>("api")
    .WithEnvironment("MAKANA_BOOTSTRAP_API_TOKENS", makanaApiTestToken)
    .WithEnvironment("DOTNET_API_DB", makanaApiDbPath)
    .WithEnvironment("MAKANA_BOOTSTRAP_USER_ID", makanaBootstrapUserId)
    .WithEnvironment("MAKANA_BOOTSTRAP_USER_LOGIN", makanaBootstrapUserLogin)
    .WithEnvironment("MAKANA_BOOTSTRAP_USER_NAME", "Aspire Test User")
    .WithEnvironment("MAKANA_BOOTSTRAP_ORG_ID", string.Empty)
    .WithEnvironment("MAKANA_BOOTSTRAP_ORG_LOGIN", string.Empty)
    .WithEnvironment("OIDC_PROVIDER", oidcProvider)
    .WithEnvironment("OIDC_MODE", oidcMode)
    .WithEnvironment("OIDC_CLIENT_ID", oidcClientId)
    .WithEnvironment("OIDC_CLIENT_SECRET", oidcClientSecret)
    .WithEnvironment("OIDC_METADATA_ADDRESS", oidcMetadataAddress)
    .WithHttpHealthCheck(path: "/health", endpointName: "http");

var webConsole = builder.AddProject<Projects.Makana_WebConsole>("app")
    .WithReference(api)
    .WithEnvironment("PULUMI_API_INTERNAL_ENDPOINT", api.GetEndpoint("http"))
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

builder.Build().Run();

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

        var localAuthority = pocketIdProxy.GetEndpoint("https");
        var localMetadataAddress = ReferenceExpression.Create($"{pocketIdProxy.GetEndpoint("http")}/.well-known/openid-configuration");

        pocketId.WithEnvironment("APP_URL", localAuthority);

        api.WithEnvironment("OIDC_AUTHORITY", localAuthority)
           .WithEnvironment("OIDC_METADATA_ADDRESS", localMetadataAddress);

        webConsole.WithEnvironment("OIDC_AUTHORITY", localAuthority)
                  .WithEnvironment("OIDC_METADATA_ADDRESS", localMetadataAddress);

        return;
    }

    api.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
    webConsole.WithEnvironment("OIDC_AUTHORITY", oidcAuthority);
}
