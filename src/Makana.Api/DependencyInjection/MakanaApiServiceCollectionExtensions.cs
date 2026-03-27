using Htmx;
using Makana.Api.Configuration;
using Makana.Api.Endpoints;
using Makana.Api.Realtime;
using Makana.Api.Security;
using Makana.Api.Storage;
using Makana.Api.Updates;
using Makana.StackUpdates.Pulumi;
using Makana.StackUpdates.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Makana.Api.DependencyInjection;

internal static class MakanaApiServiceCollectionExtensions
{
    internal const string WebConsoleCorsPolicyName = "WebConsoleCors";

    public static IServiceCollection AddMakanaApiApplication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.ConfigureHttpJsonOptions(static options =>
        {
            ConfigureJsonSerializerOptions(options.SerializerOptions);
        });

        services.AddRequestDecompression();
        services.AddMemoryCache();

        services.AddMakanaApiConfiguration(configuration);
        services.AddMakanaApiStorageServices();
        services.AddMakanaApiRealtime(environment);
        services.AddMakanaApiAuthentication();
        services.AddMakanaApiAuthorization();
        services.AddMakanaApiCors();

        return services;
    }

    private static IServiceCollection AddMakanaApiConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<MakanaApiCorsOptions>, MakanaApiCorsOptionsValidator>();
        services.AddSingleton<IValidateOptions<EphemeralStateCleanupPolicy>, EphemeralStateCleanupPolicyValidator>();
        services.AddSingleton<IValidateOptions<OidcSubjectTokenValidationOptions>, OidcSubjectTokenValidationOptionsValidator>();

        services.AddOptions<MakanaApiCorsOptions>()
            .Bind(configuration.GetSection("Cors"))
            .ValidateOnStart();

        services.AddOptions<EphemeralStateCleanupPolicy>()
            .BindConfiguration(nameof(EphemeralStateCleanupPolicy))
            .ValidateOnStart();

        services.AddOptions<OidcSubjectTokenValidationOptions>()
            .Configure(options =>
            {
                options.Authority = configuration["OIDC_AUTHORITY"] ?? string.Empty;
                options.MetadataAddress = configuration["OIDC_METADATA_ADDRESS"] ?? string.Empty;
                options.SubjectTokenAudience = configuration["OIDC_SUBJECT_TOKEN_AUDIENCE"]
                    ?? configuration["OIDC_CLIENT_ID"]
                    ?? string.Empty;
            })
            .ValidateOnStart();

        services.AddSingleton(static sp => sp.GetRequiredService<IOptions<EphemeralStateCleanupPolicy>>().Value);
        services.AddSingleton(static sp => sp.GetRequiredService<IOptions<OidcSubjectTokenValidationOptions>>().Value);

        return services;
    }

    private static IServiceCollection AddMakanaApiStorageServices(this IServiceCollection services)
    {
        services.AddSingleton<MakanaApiStorageRuntime>();
        services.AddHealthChecks()
            .AddCheck<MakanaApiStorageHealthCheck>("makana-storage");

        services.AddSingleton(static sp =>
        {
            var runtime = sp.GetRequiredService<MakanaApiStorageRuntime>();
            return new SqliteControlDatabase(runtime.ControlDatabaseConnectionString);
        });

        services.AddSingleton<StackShardDatabase>();
        services.AddSingleton<IdentityRepository>();
        services.AddSingleton<StackCatalogQueryRepository>();
        services.AddSingleton<StackCatalogCommandRepository>();
        services.AddSingleton<UpdateCommandRepository>();
        services.AddSingleton<UpdateQueryRepository>();
        services.AddSingleton<UpdateLogRepository>();
        services.AddSingleton<DeploymentRunRepository>();
        services.AddSingleton<RegistryRepository>();
        services.AddSingleton<UploadRepository>();
        services.AddSingleton<MaintenanceRepository>();
        services.AddSingleton<AuthenticatedApiContextAccessor>();
        services.AddSingleton<EndpointHelpers>();
        services.AddSingleton<ImportUpdateEventFactory>();
        services.AddSingleton<UpdateProjectionReducer>();
        services.AddSingleton<StackUpdateTranscriptMapper>();
        services.AddSingleton<StackTranscriptDeltaBuilder>();
        services.AddSingleton<StackConsoleDeltaBuilder>();
        services.AddSingleton<UpdateProjectionStateCache>();
        services.AddSingleton<UpdateViewMaterializer>();
        services.AddSingleton<StackOverviewMaterializer>();
        services.AddSingleton<VersionedUpdatePageMaterializer>();
        services.AddSingleton<IUpdateCheckpointJournalingProvider, ConformantJournalingUpdateProvider>();
        services.AddSingleton<EphemeralStateJanitor>();
        services.AddSingleton<IMakanaApiMaintenanceRunner>(static sp => sp.GetRequiredService<EphemeralStateJanitor>());
        services.AddHttpClient(nameof(OidcSubjectTokenValidator));
        services.AddSingleton<OidcSubjectTokenValidator>();
        services.AddHostedService<EphemeralStateJanitorHostedService>();

        return services;
    }

    private static IServiceCollection AddMakanaApiRealtime(this IServiceCollection services, IHostEnvironment environment)
    {
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = environment.IsDevelopment();
        })
            .AddJsonProtocol(static options =>
            {
                ConfigureJsonSerializerOptions(options.PayloadSerializerOptions);
            });
        services.AddSingleton<IStackUpdateNotifier, SignalRStackUpdateNotifier>();

        return services;
    }

    private static IServiceCollection AddMakanaApiAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(PulumiAuthenticationDefaults.Scheme)
            .AddPolicyScheme(PulumiAuthenticationDefaults.Scheme, PulumiAuthenticationDefaults.Scheme, static options =>
            {
                options.ForwardDefaultSelector = context =>
                {
                    if (PulumiAuthorizationTokenReader.TryReadAuthorizationToken(context.Request, out var kind, out _)
                        && string.Equals(kind, "update-token", StringComparison.OrdinalIgnoreCase))
                    {
                        return PulumiAuthenticationDefaults.UpdateScheme;
                    }

                    return PulumiAuthenticationDefaults.TokenScheme;
                };
            })
            .AddScheme<PulumiTokenOptions, PulumiApiTokenAuthenticationHandler>(PulumiAuthenticationDefaults.TokenScheme, static _ => { })
            .AddScheme<PulumiTokenOptions, PulumiUpdateTokenAuthenticationHandler>(PulumiAuthenticationDefaults.UpdateScheme, static _ => { });

        return services;
    }

    private static IServiceCollection AddMakanaApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
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
                policy.AddRequirements(new UpdateTokenRequirement());
            })
            .AddPolicy("OrgRouteAccess", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new OrgMemberRequirement());
            })
            .AddPolicy("StackUpdateHubAccess", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new StackUpdateHubRequirement());
            });

        services.AddSingleton<IAuthorizationHandler, ApiTokenAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, UpdateTokenForRouteAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, OrgMemberAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, StackUpdateHubAuthorizationHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, MakanaAuthorizationMiddlewareResultHandler>();

        return services;
    }

    private static IServiceCollection AddMakanaApiCors(this IServiceCollection services)
    {
        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<MakanaApiCorsOptions>>(static (options, corsOptions) =>
            {
                options.AddPolicy(
                    WebConsoleCorsPolicyName,
                    policy =>
                    {
                        var allowedOrigins = NormalizeOrigins(corsOptions.Value.AllowedOrigins);
                        if (allowedOrigins.Length == 0)
                        {
                            return;
                        }

                        policy
                            .WithOrigins(allowedOrigins)
                            .AllowAnyMethod()
                            .AllowCredentials()
                            .AllowAnyHeader()
                            .WithExposedHeaders(HtmxResponseHeaders.Keys.All);
                    });
            });

        return services;
    }

    private static void ConfigureJsonSerializerOptions(JsonSerializerOptions options)
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

    private static string[] NormalizeOrigins(IEnumerable<string?>? origins)
    {
        if (origins is null)
        {
            return [];
        }

        return origins
            .Where(static origin => !string.IsNullOrWhiteSpace(origin))
            .Select(static origin => origin!.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
