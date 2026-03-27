using Makana.Api.DependencyInjection;
using Makana.Api.Endpoints;
using Makana.Api.Middleware;
using Makana.Api.Realtime;
using Makana.Api.Storage;
using Makana.Api.Updates;
using Microsoft.AspNetCore.Routing.Constraints;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrelHttpsConfiguration();

builder.AddServiceDefaults();
builder.AddLitestreamDatabase(MakanaLitestreamNames.ControlDatabase);
builder.AddLitestreamDatabaseGroup(MakanaLitestreamNames.ApiDataRoot);

builder.Services.AddMakanaApiApplication(builder.Configuration, builder.Environment);
builder.Services.Configure<RouteOptions>(options => options.SetParameterPolicy<RegexInlineRouteConstraint>("regex"));

var app = builder.Build();

app.MapDefaultEndpoints();

var storageRuntime = app.Services.GetRequiredService<MakanaApiStorageRuntime>();
storageRuntime.InitializeDatabase(app.Services.GetRequiredService<SqliteControlDatabase>());

var identityRepository = app.Services.GetRequiredService<IdentityRepository>();

var secretKey = storageRuntime.CanServeDatabaseTraffic
    ? identityRepository.GetOrCreateSecretKey()
    : [];

app.UsePulumiMediaType();
app.UseRequestDecompression();
app.UseCors(MakanaApiServiceCollectionExtensions.WebConsoleCorsPolicyName);
app.UseMiddleware<MakanaApiStorageAvailabilityMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapCoreEndpoints(
    identityRepository,
    app.Services.GetRequiredService<StackCatalogQueryRepository>(),
    app.Services.GetRequiredService<UpdateQueryRepository>());
app.MapStackEndpoints(
    app.Services.GetRequiredService<StackCatalogQueryRepository>(),
    app.Services.GetRequiredService<StackCatalogCommandRepository>(),
    app.Services.GetRequiredService<UpdateCommandRepository>(),
    app.Services.GetRequiredService<UpdateQueryRepository>(),
    app.Services.GetRequiredService<UpdateLogRepository>(),
    secretKey);
app.MapUpdateEndpoints(
    app.Services.GetRequiredService<StackCatalogQueryRepository>(),
    app.Services.GetRequiredService<UpdateCommandRepository>(),
    app.Services.GetRequiredService<UpdateQueryRepository>(),
    app.Services.GetRequiredService<UpdateLogRepository>(),
    app.Services.GetRequiredService<IStackUpdateNotifier>(),
    app.Services.GetRequiredService<IUpdateCheckpointJournalingProvider>(),
    app.Services.GetRequiredService<EphemeralStateJanitor>());
app.MapDeploymentEndpoints(
    app.Services.GetRequiredService<StackCatalogQueryRepository>(),
    app.Services.GetRequiredService<DeploymentRunRepository>(),
    secretKey);
app.MapRegistryEndpoints(app.Services.GetRequiredService<RegistryRepository>());
app.MapUploadEndpoints(app.Services.GetRequiredService<UploadRepository>());
app.MapConsoleEndpoints();
app.MapAiTasksEndpoints();
app.MapAuditLogsEndpoints();
app.MapDataExportEndpoints();
app.MapDeploymentAutomationEndpoints();
app.MapDeploymentRunnersEndpoints();
app.MapEnvironmentsEndpoints();
app.MapInsightsAccountsEndpoints();
app.MapOidcIssuersEndpoints();
app.MapOrganizationsEndpoints();
app.MapPersonalAccessTokensEndpoints();
app.MapPolicyEndpoints();
app.MapPolicyResultsEndpoints();
app.MapRegistryAdminEndpoints();
app.MapResourceSearchEndpoints();
app.MapServicesEndpoints();
app.MapStackAdvancedEndpoints();
app.MapWebhooksEndpoints();
app.MapHub<StackUpdateHub>("/hubs/stack-updates")
    .RequireAuthorization("StackUpdateHubAccess");

await app.RunAsync();
