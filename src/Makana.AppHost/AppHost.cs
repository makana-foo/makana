#pragma warning disable ASPIRECERTIFICATES001
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Yarp;
using Aspire.Hosting.Yarp.Transforms;
using Makana.AppHost;
using Makana.AppHost.Configuration;
using Makana.AppHost.Litestream;
using Makana.AppHost.Oidc;
using Microsoft.Extensions.Configuration;

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
var publicIngressOptions = builder.Configuration
    .GetSection(PublicIngressOptions.SectionName)
    .Get<PublicIngressOptions>() ?? new PublicIngressOptions();

apiContainerOptions.Validate();

var oidc = builder.AddOidcParameters();
var makanaLitestream = builder.AddMakanaLitestreamParameters();
var oidcLitestream = builder.AddOidcLitestreamParameters();
var makanaApiDbPath = builder.AddMakanaApiDbPathParameter();
var localS3 = await builder.AddLocalS3IfNeededAsync(makanaLitestream, oidcLitestream);
var apiStorage = await builder.ResolveMakanaApiStorageContextAsync(
    makanaApiDbPath,
    apiDataOptions.UsePersistentState);
var apiLitestream = await builder.AddMakanaApiLitestreamAsync(apiStorage, makanaLitestream, localS3);
var apiRuntimeUser = AppHostContainerUsers.Api;

var api = builder.AddProject<Projects.Makana_Api>("api", options =>
    {
        options.ExcludeLaunchProfile = true;
    })
    .WithHttpEndpoint()
    .PublishAsDockerFile(resource =>
    {
        var apiDataVolume = apiStorage.VolumeName
            ?? throw new InvalidOperationException("Publish-mode API storage requires a shared volume.");

        resource.WithDockerfile("api", "../", "Makana.Api/Dockerfile")
            .WithBuildArg("PUBLISH_AOT", apiContainerOptions.PublishAot.ToString().ToLowerInvariant())
            .WithBuildArg("FINAL_BASE_IMAGE", apiContainerOptions.ResolveFinalBaseImage())
            .WithHttpEndpoint(targetPort: 8080);

        resource.WithVolume(apiDataVolume, apiStorage.DataPath)
            .WithContainerFiles(
                apiStorage.DataPath,
                [
                    new ContainerDirectory
                    {
                        Name = "stacks",
                        Owner = apiRuntimeUser.UserId,
                        Group = apiRuntimeUser.GroupId,
                    }
                ],
                apiRuntimeUser.UserId,
                apiRuntimeUser.GroupId);
    })
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", applicationEnvironment)
    .WithEnvironment("DOTNET_ENVIRONMENT", applicationEnvironment)
    .WithEnvironment("Makana__Storage__ReplicaState", "absent")
    .WaitFor(apiLitestream)
    .WithMakanaApiLitestreamEnvironment(apiStorage)
    .WithHttpHealthCheck(path: "/health", endpointName: "http");

var webConsole = builder.AddProject<Projects.Makana_WebConsole>("app", options =>
    {
        options.ExcludeLaunchProfile = true;
    })
    .WithHttpEndpoint()
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", applicationEnvironment)
    .WithEnvironment("DOTNET_ENVIRONMENT", applicationEnvironment)
    .WithReference(api)
    .WithHttpHealthCheck(path: "/health", endpointName: "http");

if (!string.IsNullOrWhiteSpace(testingWebConsoleApiToken))
{
    webConsole = webConsole.WithEnvironment("MAKANA_WEBCONSOLE_API_TOKEN", testingWebConsoleApiToken);
}

var publicIngress = builder.AddYarp("public-ingress")
    .WithHttpsEndpoint()
    .WithHttpsDeveloperCertificate();

if (string.Equals(applicationEnvironment, "Development", StringComparison.OrdinalIgnoreCase) &&
    publicIngressOptions.HttpsPort is { } developmentIngressPort)
{
    publicIngress = publicIngress.WithHostHttpsPort(developmentIngressPort);
}

api = api.WithDashboardUrlFromEndpoint(
    publicIngress,
    KnownNetworkIdentifiers.LocalhostNetwork,
    publicIngressOptions.ApiHost,
    "Public URL");

webConsole = webConsole
    .WithDashboardUrlFromEndpoint(
        publicIngress,
        KnownNetworkIdentifiers.LocalhostNetwork,
        publicIngressOptions.AppHost,
        "Public URL")
    .WithEnvironmentUrlFromEndpoint("Makana__ApiPublicUrl", publicIngress, KnownNetworkIdentifiers.LocalhostNetwork, publicIngressOptions.ApiHost, "https")
    .WithEnvironmentUrlFromEndpoint("Makana__WebConsolePublicUrl", publicIngress, KnownNetworkIdentifiers.LocalhostNetwork, publicIngressOptions.AppHost, "https")
    .WithEnvironmentUrlFromEndpoint("Makana__WebConsoleLoginUrl", publicIngress, KnownNetworkIdentifiers.LocalhostNetwork, publicIngressOptions.AppHost, "https");

var oidcEndpoints = await builder.AddOidcResourcesAsync(
    oidc,
    pocketIdOptions,
    dexOptions,
    oidcLitestream,
    localS3,
    publicIngress,
    publicIngressOptions);

if (oidcEndpoints is not null)
{
    oidcEndpoints = oidcEndpoints with
    {
        AuthorityResource = oidcEndpoints.AuthorityResource.WithDashboardUrlFromEndpoint(
            publicIngress,
            KnownNetworkIdentifiers.LocalhostNetwork,
            publicIngressOptions.AuthHost,
            "Public URL")
    };

    api = api.WaitFor(oidcEndpoints.AuthorityResource);
    webConsole = webConsole.WaitFor(oidcEndpoints.AuthorityResource);
}

api = api
    .WithOidcEnvironment(oidc, oidcEndpoints, includeApiAudiences: true)
    .WithBrowserOriginsFromEndpoint(
        publicIngress,
        KnownNetworkIdentifiers.LocalhostNetwork,
        configuredHosts:
        [
            publicIngressOptions.AppHost,
            .. builder.Configuration.GetSection("Cors").GetSection("AllowedOrigins").Get<string[]?>() ?? []
        ]);

if (oidcEndpoints?.DexResource is not null)
{
    api = api.WithEnvironment("Dex__GrpcAddress", oidcEndpoints.DexResource.GetEndpointUrlReference("grpc", KnownNetworkIdentifiers.LocalhostNetwork));
}

webConsole = webConsole.WithOidcEnvironment(oidc, oidcEndpoints);

publicIngress = publicIngress
    .WaitFor(api)
    .WaitFor(webConsole)
    .WithConfiguration(yarp =>
    {
        ConfigureIngressRoute(yarp.AddRoute(webConsole)
            .WithMatchHosts([publicIngressOptions.AppHost]));

        ConfigureIngressRoute(yarp.AddRoute(api)
            .WithMatchHosts([publicIngressOptions.ApiHost]));

        if (oidcEndpoints?.DexResource is not null)
        {
            var dexEndpoint = oidcEndpoints.DexResource.GetEndpoint("http", KnownNetworkIdentifiers.DefaultAspireContainerNetwork);

            ConfigureIngressRoute(yarp.AddRoute("/.well-known/openid-configuration", dexEndpoint)
                .WithMatchHosts([publicIngressOptions.AuthHost]));
            ConfigureIngressRoute(yarp.AddRoute("/auth/{**catch-all}", dexEndpoint)
                .WithMatchHosts([publicIngressOptions.AuthHost]));
            ConfigureIngressRoute(yarp.AddRoute("/keys", dexEndpoint)
                .WithMatchHosts([publicIngressOptions.AuthHost]));
            ConfigureIngressRoute(yarp.AddRoute("/token", dexEndpoint)
                .WithMatchHosts([publicIngressOptions.AuthHost]));
            ConfigureIngressRoute(yarp.AddRoute("/callback", dexEndpoint)
                .WithMatchHosts([publicIngressOptions.AuthHost]));
            ConfigureIngressRoute(yarp.AddRoute("/logout", dexEndpoint)
                .WithMatchHosts([publicIngressOptions.AuthHost]));
            ConfigureIngressRoute(yarp.AddRoute("/logout/{**catch-all}", dexEndpoint)
                .WithMatchHosts([publicIngressOptions.AuthHost]));
        }

        if (oidcEndpoints?.PocketIdResource is not null)
        {
            var pocketIdEndpoint = oidcEndpoints.PocketIdResource.GetEndpoint("http", KnownNetworkIdentifiers.DefaultAspireContainerNetwork);

            ConfigureIngressRoute(yarp.AddRoute("/{**catch-all}", pocketIdEndpoint)
                .WithMatchHosts([publicIngressOptions.AuthHost])
                .WithOrder(100));
        }
    });

var app = builder.Build();
await app.RunAsync();

static void ConfigureIngressRoute(Aspire.Hosting.Yarp.YarpRoute route)
{
    route
        .WithTransformRequestHeader("X-Makana-Ingress", "public-ingress", append: false);
}
#pragma warning restore ASPIRECERTIFICATES001
