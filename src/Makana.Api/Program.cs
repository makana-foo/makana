using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Claims;
using Makana.Api.Endpoints;
using Makana.Api.Middleware;
using Makana.Api.Security;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.IdentityModel.Tokens;
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

builder.Services.AddSingleton(new Database($"Data Source={dbPath}"));
builder.Services.AddSingleton<BackendStore>();

var casdoorUrl = builder.Configuration["CASDOOR_URL"]
    ?? throw new InvalidOperationException("CASDOOR_URL is required for API authentication.");

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
                return JwtBearerDefaults.AuthenticationScheme;
            }

            return PulumiAuthenticationDefaults.Scheme;
        };
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Authority = casdoorUrl.TrimEnd('/');
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = false,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity &&
                    !identity.HasClaim(c => c.Type == PulumiAuthenticationDefaults.AuthKindClaim))
                {
                    identity.AddClaim(new Claim(PulumiAuthenticationDefaults.AuthKindClaim, "bearer"));
                }

                return Task.CompletedTask;
            }
        };
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

var bootstrapTokens = builder.Configuration["MAKANA_BOOTSTRAP_API_TOKENS"];
if (!string.IsNullOrWhiteSpace(bootstrapTokens))
{
    var bootstrapUserId = builder.Configuration["MAKANA_BOOTSTRAP_USER_ID"] ?? "bootstrap-user";
    var bootstrapUserLogin = builder.Configuration["MAKANA_BOOTSTRAP_USER_LOGIN"];
    var bootstrapUserName = builder.Configuration["MAKANA_BOOTSTRAP_USER_NAME"] ?? "Bootstrap User";
    var bootstrapUserEmail = builder.Configuration["MAKANA_BOOTSTRAP_USER_EMAIL"];
    var bootstrapUserAvatar = builder.Configuration["MAKANA_BOOTSTRAP_USER_AVATAR_URL"] ?? string.Empty;
    var bootstrapOrgLogin = builder.Configuration["MAKANA_BOOTSTRAP_ORG_LOGIN"];
    var bootstrapOrgId = builder.Configuration["MAKANA_BOOTSTRAP_ORG_ID"];

    if (string.IsNullOrWhiteSpace(bootstrapUserLogin))
    {
        throw new InvalidOperationException(
            "MAKANA_BOOTSTRAP_USER_LOGIN is required when MAKANA_BOOTSTRAP_API_TOKENS is configured.");
    }

    var existingUserId = await store.GetUserIdByLoginAsync(bootstrapUserLogin);
    if (!string.IsNullOrWhiteSpace(existingUserId))
    {
        bootstrapUserId = existingUserId;
    }

    string? resolvedOrgId = null;
    if (!string.IsNullOrWhiteSpace(bootstrapOrgLogin))
    {
        var existingOrgId = await store.GetOrgIdByLoginAsync(bootstrapOrgLogin);
        resolvedOrgId = !string.IsNullOrWhiteSpace(existingOrgId)
            ? existingOrgId
            : (!string.IsNullOrWhiteSpace(bootstrapOrgId) ? bootstrapOrgId : "org-" + Guid.NewGuid().ToString("N"));
    }

    await store.EnsureUserAsync(
        bootstrapUserId,
        bootstrapUserLogin,
        bootstrapUserName,
        bootstrapUserEmail,
        bootstrapUserAvatar);
    if (!string.IsNullOrWhiteSpace(bootstrapOrgLogin) && !string.IsNullOrWhiteSpace(resolvedOrgId))
    {
        await store.EnsureOrgAsync(resolvedOrgId, bootstrapOrgLogin);
        await store.EnsureOrgMembershipAsync(resolvedOrgId, bootstrapUserId);
    }

    foreach (var token in bootstrapTokens.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
    {
        await store.EnsureApiTokenAsync(token, bootstrapUserId, resolvedOrgId, null);
    }
}

var secretKey = await store.GetOrCreateSecretKeyAsync();

app.UsePulumiMediaType();
app.UseGzipRequestDecompression();
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    branch =>
    {
        branch.UseAuthentication();
        branch.UseAuthorization();
    });

app.MapCoreEndpoints(store);
app.MapStackEndpoints(store, secretKey);
app.MapUpdateEndpoints(store);
app.MapDeploymentEndpoints(store, secretKey);
app.MapRegistryEndpoints(store);
app.MapUploadEndpoints(store);
app.MapConsoleEndpoints();

app.Run();
