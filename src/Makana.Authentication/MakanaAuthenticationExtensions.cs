using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Makana.Authentication;

public static class MakanaAuthenticationExtensions
{
    public static IServiceCollection AddMakanaOidcAuthority(
        this IServiceCollection services,
        string name,
        Action<MakanaOidcAuthorityOptions> configure)
    {
        services.AddOptions<MakanaOidcAuthorityOptions>(name)
            .Configure(configure)
            .ValidateOnStart();

        EnsureCoreOidcServices(services);
        services.AddSingleton(new OidcAuthorityRegistration(name));

        return services;
    }

    public static IServiceCollection AddMakanaOidcTokenValidation(
        this IServiceCollection services,
        string name,
        Action<MakanaOidcTokenValidationOptions> configure)
    {
        services.AddOptions<MakanaOidcTokenValidationOptions>(name)
            .Configure(configure)
            .ValidateOnStart();

        EnsureCoreOidcServices(services);

        return services;
    }

    public static AuthenticationBuilder AddMakanaOpenIdConnect(
        this AuthenticationBuilder builder,
        string scheme,
        string authorityName,
        Action<OpenIdConnectOptions>? configure = null)
    {
        builder.AddOpenIdConnect(scheme, options => configure?.Invoke(options));
        builder.Services.AddOptions<OpenIdConnectOptions>(scheme)
            .PostConfigure<OidcAuthorityRegistry>((options, registry) =>
            {
                var authority = registry.GetAuthority(authorityName);
                options.Authority = authority.Options.Authority;
                options.MetadataAddress = authority.Options.MetadataAddress;
                options.RequireHttpsMetadata = authority.Options.RequireHttpsMetadata;
                options.ConfigurationManager = authority.ConfigurationManager;
            });

        return builder;
    }

    public static IServiceCollection AddMakanaDexConnectorProvisioning(
        this IServiceCollection services,
        Action<DexConnectorProvisioningOptions> configure)
    {
        services.AddOptions<DexConnectorProvisioningOptions>()
            .Configure(configure)
            .ValidateOnStart();
        services.TryAddSingleton<IValidateOptions<DexConnectorProvisioningOptions>, DexConnectorProvisioningOptionsValidator>();
        services.TryAddSingleton<IDexConnectorProvisioningService, DexConnectorProvisioningService>();
        return services;
    }

    public static AuthenticationBuilder AddMakanaJwtBearer(
        this AuthenticationBuilder builder,
        string scheme,
        string authorityName,
        Action<JwtBearerOptions>? configure = null)
    {
        builder.AddJwtBearer(scheme, options => configure?.Invoke(options));
        builder.Services.AddOptions<JwtBearerOptions>(scheme)
            .PostConfigure<OidcAuthorityRegistry>((options, registry) =>
            {
                var authority = registry.GetAuthority(authorityName);
                options.Authority = authority.Options.Authority;
                options.MetadataAddress = authority.Options.MetadataAddress;
                options.RequireHttpsMetadata = authority.Options.RequireHttpsMetadata;
                options.ConfigurationManager = authority.ConfigurationManager;
            });

        return builder;
    }

    private static void EnsureCoreOidcServices(IServiceCollection services)
    {
        services.TryAddSingleton<IValidateOptions<MakanaOidcAuthorityOptions>, MakanaOidcAuthorityOptionsValidator>();
        services.TryAddSingleton<IValidateOptions<MakanaOidcTokenValidationOptions>, MakanaOidcTokenValidationOptionsValidator>();
        services.AddHttpClient(MakanaOidcDefaults.ConfigurationHttpClientName);
        services.TryAddSingleton<OidcAuthorityRegistry>();
        services.TryAddSingleton<IOidcTokenValidationService, OidcTokenValidationService>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OidcLifecycleHostedService>());

        if (!services.Any(static descriptor => descriptor.ServiceType == typeof(OidcLifecycleHealthCheckRegistrationMarker)))
        {
            services.AddSingleton<OidcLifecycleHealthCheckRegistrationMarker>();
            services.AddHealthChecks()
                .AddCheck<OidcLifecycleHealthCheck>("oidc-configuration");
        }
    }
}
