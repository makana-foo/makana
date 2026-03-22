using System.Text.Json;
using System.Text.Json.Serialization;
using Makana.Api.Endpoints;
using Makana.Api.Middleware;
using Makana.Api.Realtime;
using Makana.Api.Security;
using Makana.Api.Storage;
using Makana.Api.Updates;
using Makana.StackUpdates.Pulumi;
using Makana.StackUpdates.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.TypeInfoResolverChain.Add(CoreEndpointsJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(StackEndpointsJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(UpdateEndpointsJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(DeploymentEndpointsJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(RegistryEndpointsJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(ConsoleEndpointsJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(SecurityJsonContext.Default);
    options.SerializerOptions.TypeInfoResolverChain.Add(PulumiEngineEventJsonContext.Default);
});

var dbPath = Environment.GetEnvironmentVariable("DOTNET_API_DB");
if (string.IsNullOrWhiteSpace(dbPath))
{
    throw new InvalidOperationException("DOTNET_API_DB is required.");
}
var dbDirectory = Path.GetDirectoryName(dbPath);
if (!string.IsNullOrWhiteSpace(dbDirectory))
{
    Directory.CreateDirectory(dbDirectory);
}

builder.Services.AddSingleton(new Database($"Data Source={dbPath}"));
builder.Services.AddSingleton<BackendStore>();
builder.Services.AddSingleton<EndpointHelpers>();
builder.Services.AddSingleton<NamespaceRoutingHelpers>();
builder.Services.AddSingleton<ImportUpdateEventFactory>();
builder.Services.AddSingleton<UpdateProjectionReducer>();
builder.Services.AddSingleton<StackUpdateTranscriptMapper>();
builder.Services.AddSingleton<StackTranscriptDeltaBuilder>();
builder.Services.AddSingleton<StackConsoleDeltaBuilder>();
builder.Services.AddSingleton<UpdateProjectionStateCache>();
builder.Services.AddSingleton<UpdateViewMaterializer>();
builder.Services.AddSingleton<StackOverviewMaterializer>();
builder.Services.AddSingleton<VersionedUpdatePageMaterializer>();
builder.Services.AddSingleton<IUpdateCheckpointJournalingProvider, ConformantJournalingUpdateProvider>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<OidcUserInfoClient>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IStackUpdateNotifier, SignalRStackUpdateNotifier>();

var oidcProvider = builder.Configuration["OIDC_PROVIDER"] ?? "oidc";
var normalizedOidcProvider = NormalizeProviderKey(oidcProvider);
var oidcAuthority = builder.Configuration["OIDC_AUTHORITY"]
    ?? throw new InvalidOperationException("OIDC_AUTHORITY is required for API authentication.");
var oidcMetadataAddress = builder.Configuration["OIDC_METADATA_ADDRESS"];
var oidcApiAudience = builder.Configuration["OIDC_API_AUDIENCE"]
    ?? throw new InvalidOperationException("OIDC_API_AUDIENCE is required for API authentication.");
var oidcSubjectTokenAudience = builder.Configuration["OIDC_SUBJECT_TOKEN_AUDIENCE"]
    ?? throw new InvalidOperationException("OIDC_SUBJECT_TOKEN_AUDIENCE is required for subject-token validation.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = PulumiAuthenticationDefaults.CombinedScheme;
        options.DefaultAuthenticateScheme = PulumiAuthenticationDefaults.CombinedScheme;
        options.DefaultChallengeScheme = PulumiAuthenticationDefaults.CombinedScheme;
    })
    .AddPolicyScheme(PulumiAuthenticationDefaults.CombinedScheme, "Combined API auth", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            var auth = context.Request.Headers.Authorization.ToString();
            if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return PulumiAuthenticationDefaults.OidcApiBearerScheme;
            }

            return PulumiAuthenticationDefaults.Scheme;
        };
    })
    .AddJwtBearer(PulumiAuthenticationDefaults.OidcApiBearerScheme, options =>
    {
        JwtBearerConfiguration.ConfigureApiBearer(
            options,
            oidcAuthority,
            oidcMetadataAddress,
            oidcApiAudience,
            normalizedOidcProvider);
    })
    .AddJwtBearer(PulumiAuthenticationDefaults.OidcSubjectTokenScheme, options =>
    {
        JwtBearerConfiguration.ConfigureSubjectTokenBearer(
            options,
            oidcAuthority,
            oidcMetadataAddress,
            oidcSubjectTokenAudience,
            normalizedOidcProvider);
    })
    .AddScheme<AuthenticationSchemeOptions, PulumiTokenAuthenticationHandler>(
        PulumiAuthenticationDefaults.Scheme,
        _ => { });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("ApiToken", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ApiTokenRequirement());
    });
    options.AddPolicy("UpdateTokenForRoute", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new UpdateTokenForRouteRequirement());
    });
    options.AddPolicy("OrgRouteAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new OrgRouteAccessRequirement());
    });
});
builder.Services.AddSingleton<IAuthorizationHandler, ApiTokenAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, UpdateTokenForRouteAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, OrgRouteAccessAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, MakanaAuthorizationMiddlewareResultHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();

var database = app.Services.GetRequiredService<Database>();
await database.InitializeAsync();
var store = app.Services.GetRequiredService<BackendStore>();

var secretKey = await store.GetOrCreateSecretKeyAsync();
var oidcSettings = new CoreEndpoints.OidcSettings(
    ProviderKey: normalizedOidcProvider);

app.UsePulumiMediaType();
app.UseGzipRequestDecompression();
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    branch =>
    {
        branch.UseAuthentication();
        branch.UseAuthorization();
    });

// Endpoint registration uses Minimal API Delegate overloads that are annotated as
// requiring dynamic code/unreferenced code. Scope suppression narrowly to startup.
#pragma warning disable IL2026, IL3050
app.MapCoreEndpoints(store, oidcSettings, app.Services.GetRequiredService<OidcUserInfoClient>());
app.MapStackEndpoints(store, secretKey);
app.MapUpdateEndpoints(
    store,
    app.Services.GetRequiredService<IStackUpdateNotifier>(),
    app.Services.GetRequiredService<IUpdateCheckpointJournalingProvider>());
app.MapDeploymentEndpoints(store, secretKey);
app.MapRegistryEndpoints(store);
app.MapUploadEndpoints(store);
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
app.MapHub<StackUpdateHub>("/hubs/stack-updates");
#pragma warning restore IL2026, IL3050

app.Run();

static string NormalizeProviderKey(string provider)
{
    var value = provider.Trim().ToLowerInvariant();
    return string.IsNullOrWhiteSpace(value) ? "oidc" : value;
}
