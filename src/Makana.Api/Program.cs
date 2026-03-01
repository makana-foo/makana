using System.Text.Json;
using System.Text.Json.Serialization;
using Makana.Api.Endpoints;
using Makana.Api.Middleware;
using Makana.Api.Security;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
// codex resume 019b8fe8-0185-74c3-94c9-e00eb91dc755
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
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<OidcUserInfoClient>();

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

app.MapCoreEndpoints(store, oidcSettings, app.Services.GetRequiredService<OidcUserInfoClient>());
app.MapStackEndpoints(store, secretKey);
app.MapUpdateEndpoints(store);
app.MapDeploymentEndpoints(store, secretKey);
app.MapRegistryEndpoints(store);
app.MapUploadEndpoints(store);
app.MapConsoleEndpoints();

app.Run();

static string NormalizeProviderKey(string provider)
{
    var value = provider.Trim().ToLowerInvariant();
    return string.IsNullOrWhiteSpace(value) ? "oidc" : value;
}
