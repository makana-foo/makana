using Htmx;
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
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    ConfigureJsonSerializerOptions(options.SerializerOptions);
});

builder.Services.AddRequestDecompression(/*can optionally limit to gzip here*/);
builder.Services.AddMemoryCache();
//builder.Services.AddHttpClient();

builder.Services.AddSingleton<SqliteControlDatabase>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["DOTNET_API_DB"] switch
    {
        string dbPath => $"Data Source={dbPath}",
        _ => throw new ArgumentException("Missing DOTNET_API_DB")
    };
    return new SqliteControlDatabase(connectionString);
});
builder.Services.AddOptions<EphemeralStateCleanupPolicy>();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<EphemeralStateCleanupPolicy>>().Value);
builder.Services.AddSingleton<StackShardDatabase>();
builder.Services.AddSingleton<IdentityRepository>();
builder.Services.AddSingleton<StackCatalogQueryRepository>();
builder.Services.AddSingleton<StackCatalogCommandRepository>();
builder.Services.AddSingleton<UpdateCommandRepository>();
builder.Services.AddSingleton<UpdateQueryRepository>();
builder.Services.AddSingleton<UpdateLogRepository>();
builder.Services.AddSingleton<DeploymentRunRepository>();
builder.Services.AddSingleton<RegistryRepository>();
builder.Services.AddSingleton<UploadRepository>();
builder.Services.AddSingleton<MaintenanceRepository>();
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
builder.Services.AddSingleton<EphemeralStateJanitor>();
builder.Services.AddHostedService<EphemeralStateJanitorHostedService>();
builder.Services.AddSingleton<OidcUserInfoClient>();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
})
    .AddJsonProtocol(options =>
    {
        ConfigureJsonSerializerOptions(options.PayloadSerializerOptions);
    });
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
var webConsoleCorsOrigins = builder.Configuration
    .GetSection("Cors")
    .GetSection("AllowedOrigins")
    .Get<string[]?>();

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
            string? auth = context.Request.Headers.Authorization;
            if (auth?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) is true)
            {
                return PulumiAuthenticationDefaults.OidcApiBearerScheme;
            }

            if (context.Request.Path.StartsWithSegments("/hubs/stack-updates") &&
                !string.IsNullOrWhiteSpace(context.Request.Query["access_token"]))
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

builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build())
    .AddPolicy("ApiToken", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ApiTokenRequirement());
    })
    .AddPolicy("UpdateTokenForRoute", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new UpdateTokenForRouteRequirement());
    })
    .AddPolicy("OrgRouteAccess", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new OrgRouteAccessRequirement());
    });

const string WebConsoleCorsPolicy = "WebConsoleCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        WebConsoleCorsPolicy,
        policy =>
        {
            if (webConsoleCorsOrigins is not { Length: > 0 })
            {
                return;
            }

            policy
                .WithOrigins(webConsoleCorsOrigins)
                .AllowAnyMethod()
                .AllowCredentials()
                .AllowAnyHeader()
                .WithExposedHeaders(HtmxResponseHeaders.Keys.All);
        });
});

builder.Services.AddSingleton<IAuthorizationHandler, ApiTokenAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, UpdateTokenForRouteAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, OrgRouteAccessAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, MakanaAuthorizationMiddlewareResultHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();

var database = app.Services.GetRequiredService<SqliteControlDatabase>();
database.Initialize();
var identityRepository = app.Services.GetRequiredService<IdentityRepository>();

var secretKey = identityRepository.GetOrCreateSecretKey();
var oidcSettings = new CoreEndpoints.OidcSettings(
    ProviderKey: normalizedOidcProvider);

app.UsePulumiMediaType();
app.UseRequestDecompression();
app.UseCors(WebConsoleCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();

// Endpoint registration uses Minimal API Delegate overloads that are annotated as
// requiring dynamic code/unreferenced code. Scope suppression narrowly to startup.
#pragma warning disable IL2026, IL3050
app.MapCoreEndpoints(
    identityRepository,
    app.Services.GetRequiredService<StackCatalogQueryRepository>(),
    app.Services.GetRequiredService<UpdateQueryRepository>(),
    oidcSettings,
    app.Services.GetRequiredService<OidcUserInfoClient>());
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
    app.Services.GetRequiredService<EphemeralStateJanitor>(),
    identityRepository,
    oidcSettings,
    app.Services.GetRequiredService<OidcUserInfoClient>());
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
app.MapHub<StackUpdateHub>("/hubs/stack-updates");
#pragma warning restore IL2026, IL3050

app.Run();

static string NormalizeProviderKey(string provider)
{
    var value = provider.Trim().ToLowerInvariant();
    return string.IsNullOrWhiteSpace(value) ? "oidc" : value;
}

static void ConfigureJsonSerializerOptions(JsonSerializerOptions options)
{
    options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.TypeInfoResolverChain.Add(CoreEndpointsJsonContext.Default);
    options.TypeInfoResolverChain.Add(StackEndpointsJsonContext.Default);
    options.TypeInfoResolverChain.Add(UpdateEndpointsJsonContext.Default);
    options.TypeInfoResolverChain.Add(DeploymentEndpointsJsonContext.Default);
    options.TypeInfoResolverChain.Add(RegistryEndpointsJsonContext.Default);
    options.TypeInfoResolverChain.Add(ConsoleEndpointsJsonContext.Default);
    options.TypeInfoResolverChain.Add(SecurityJsonContext.Default);
    options.TypeInfoResolverChain.Add(PulumiEngineEventJsonContext.Default);
}
