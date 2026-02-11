using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var useLocalCasdoorContainer = builder.Configuration.GetValue("USE_LOCAL_CASDOOR_CONTAINER", true);
var configuredCasdoorUrl = builder.Configuration["CASDOOR_URL"];
var casdoorClientId = builder.AddParameter("casdoor-client-id", secret: true); //2ce82396c859cb1a2750
var casdoorClientSecret = builder.AddParameter("casdoor-client-secret", secret: true); //984614599d46bea9ab0389f4feea65ddfce56bcb
var casdoorPublicUrl = builder.AddParameter("casdoor-public-url", "http://localhost:8000");
var makanaApiTestToken = builder.AddParameter("makana-api-test-token", secret: true);
var makanaBootstrapUserId = builder.AddParameter("makana-bootstrap-user-id", "aspire-test-user");
var makanaBootstrapUserLogin = builder.AddParameter("makana-bootstrap-user-login", "local");

var makanaApiDataDir = Path.Combine(builder.Environment.ContentRootPath, ".data", "makana-api");
Directory.CreateDirectory(makanaApiDataDir);

var makanaApiDbPath = builder.AddParameter("makana-api-db-path", Path.Combine(makanaApiDataDir, "makana.db"));

var api = builder.AddProject<Projects.Makana_Api>("api")
    .WithEnvironment("MAKANA_BOOTSTRAP_API_TOKENS", makanaApiTestToken)
    .WithEnvironment("DOTNET_API_DB", makanaApiDbPath)
    .WithEnvironment("MAKANA_BOOTSTRAP_USER_ID", makanaBootstrapUserId)
    .WithEnvironment("MAKANA_BOOTSTRAP_USER_LOGIN", makanaBootstrapUserLogin)
    .WithEnvironment("MAKANA_BOOTSTRAP_USER_NAME", "Aspire Test User")
    .WithEnvironment("MAKANA_BOOTSTRAP_ORG_ID", string.Empty)
    .WithEnvironment("MAKANA_BOOTSTRAP_ORG_LOGIN", string.Empty)
    .WithHttpHealthCheck(path: "/health", endpointName: "http");

var webConsole = builder.AddProject<Projects.Makana_WebConsole>("app")
    .WithReference(api)
    .WithEnvironment("PULUMI_API_INTERNAL_ENDPOINT", api.GetEndpoint("http"))
    .WithEnvironment("CASDOOR_CLIENT_ID", casdoorClientId)
    .WithEnvironment("CASDOOR_CLIENT_SECRET", casdoorClientSecret)
    .WithHttpHealthCheck(path: "/health", endpointName: "http")
    .WaitFor(api);

if (useLocalCasdoorContainer)
{
    var casdoorDataDir = Path.Combine(builder.Environment.ContentRootPath, ".data", "casdoor");
    Directory.CreateDirectory(casdoorDataDir);

    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(
            casdoorDataDir,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
    }

    var casdoor = builder
        .AddContainer("casdoor", "casbin/casdoor", "latest")
        .WithHttpEndpoint(port: 8000, targetPort: 8000, name: "http")
        .WithBindMount(casdoorDataDir, "/data")
        .WithEnvironment("RUNNING_IN_DOCKER", "true")
        .WithEnvironment("driverName", "sqlite")
        .WithEnvironment("dataSourceName", "file:/data/casdoor.db?cache=shared")
        .WithEnvironment("dbName", "casdoor")
        .WithHttpHealthCheck(path: "/.well-known/openid-configuration", endpointName: "http");

    casdoor.WithEnvironment("origin", casdoorPublicUrl);
    casdoor.WithEnvironment("originFrontend", casdoorPublicUrl);

    api.WithEnvironment("AUTH_PROVIDER", "casdoor")
       .WithEnvironment("CASDOOR_URL", casdoor.GetEndpoint("http"))
       .WaitFor(casdoor);

    webConsole.WithEnvironment("CASDOOR_URL", casdoor.GetEndpoint("http"))
              .WaitFor(casdoor);
}
else
{
    if (string.IsNullOrWhiteSpace(configuredCasdoorUrl))
    {
        throw new InvalidOperationException("CASDOOR_URL must be configured when USE_LOCAL_CASDOOR_CONTAINER is false.");
    }

    api.WithEnvironment("AUTH_PROVIDER", "casdoor")
       .WithEnvironment("CASDOOR_URL", configuredCasdoorUrl);

    webConsole.WithEnvironment("CASDOOR_URL", configuredCasdoorUrl);
}

builder.Build().Run();
