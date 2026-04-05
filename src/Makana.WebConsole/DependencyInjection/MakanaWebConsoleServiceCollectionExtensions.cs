using Htmx;
using Makana.Authentication;
using Makana.Compatibility;
using Makana.WebConsole.Configuration;
using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Net.Http.Headers;

namespace Makana.WebConsole.DependencyInjection;

internal static class MakanaWebConsoleServiceCollectionExtensions
{
    private const string ReauthenticationRequiredItemKey = "makana.auth.reauthenticate";
    private const int ExpirationSkewMinutes = 1;

    public static IServiceCollection AddMakanaWebConsoleApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddMakanaWebConsoleConfiguration(configuration);
        services.AddMakanaWebConsoleClients();
        services.AddMakanaWebConsoleAuthentication(configuration);

        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddTransient<PulumiApiAuthHeaderHandler>();
        services.AddScoped<WebConsoleApiTokenAccessor>();
        services.AddScoped<ConsoleContextService>();
        services.AddScoped<OrganizationSsoReauthenticationService>();
        services.AddSingleton<UserAvatarService>();

        services.Configure<RouteOptions>(configuration.GetSection(nameof(RouteOptions)));

        services.AddAuthorization();
        services.AddRazorPages(options =>
        {
            options.Conventions.AuthorizeFolder("/");
            options.Conventions.AllowAnonymousToPage("/Splash");
            options.Conventions.AllowAnonymousToPage("/Login");
            options.Conventions.AllowAnonymousToPage("/Reauthenticate");
            options.Conventions.AllowAnonymousToPage("/Error");

            options.Conventions.AddPageRoute("/Stacks/Details", "/{owner}/stacks/{project}/{stack}/{runType}/{updateRef}");
        });

        return services;
    }

    private static IServiceCollection AddMakanaWebConsoleConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<WebConsoleApiOptions>, WebConsoleApiOptionsValidator>();
        services.AddSingleton<IValidateOptions<WebConsoleAuthenticationOptions>, WebConsoleAuthenticationOptionsValidator>();
        services.AddSingleton<WebConsoleLoginProviderCatalog>();

        services.AddOptions<WebConsoleApiOptions>()
            .Configure<IConfiguration>(static (options, configuration) =>
            {
                options.BrowserBaseUrl = configuration["API_HTTPS"];
                options.ApiToken = configuration["MAKANA_WEBCONSOLE_API_TOKEN"];
                options.PublicApiUrl = configuration["Makana:ApiPublicUrl"];
                options.PublicWebConsoleUrl = configuration["Makana:WebConsolePublicUrl"];
                options.PublicWebConsoleLoginUrl = configuration["Makana:WebConsoleLoginUrl"];
            })
            .ValidateOnStart();

        services.AddOptions<WebConsoleAuthenticationOptions>()
            .Bind(configuration.GetSection(WebConsoleAuthenticationOptions.SectionName))
            .ValidateOnStart();

        services.AddMakanaOidcAuthority(
            MakanaOidcDefaults.DefaultAuthorityName,
            options =>
            {
                options.Authority = configuration["OIDC_AUTHORITY"] ?? string.Empty;
                options.MetadataAddress = configuration["OIDC_METADATA_ADDRESS"] ?? string.Empty;
                options.BackchannelBaseUrl = configuration["OIDC_BACKCHANNEL_BASE_URL"] ?? string.Empty;
                options.RequireHttpsMetadata = ResolveRequireHttpsMetadata(
                    configuration["OIDC_METADATA_ADDRESS"],
                    configuration["OIDC_AUTHORITY"]);
            });

        return services;

        static bool ResolveRequireHttpsMetadata(string? metadataAddress, string? authority)
        {
            var candidate = string.IsNullOrWhiteSpace(metadataAddress) ? authority : metadataAddress;
            return !candidate?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ?? true;
        }
    }

    private static IServiceCollection AddMakanaWebConsoleClients(this IServiceCollection services)
    {
        services.AddHttpClient<PulumiApiClient>(ConfigurePulumiHttpClient)
            .AddHttpMessageHandler<PulumiApiAuthHeaderHandler>();
        services.AddHttpClient<PulumiCliLoginService>(ConfigurePulumiHttpClient);
        services.AddHttpClient<IWebConsoleApiTokenExchangeService, WebConsoleApiTokenExchangeService>(ConfigurePulumiHttpClient);

        return services;
    }

    private static void ConfigurePulumiHttpClient(IServiceProvider services, HttpClient httpClient)
    {
        _ = services.GetRequiredService<IOptions<WebConsoleApiOptions>>().Value;
        httpClient.BaseAddress = new Uri("http://api", UriKind.Absolute);
        httpClient.DefaultRequestHeaders.Accept.Clear();
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(PulumiHeaders.Accept));
    }

    private static IServiceCollection AddMakanaWebConsoleAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddMakanaOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, MakanaOidcDefaults.DefaultAuthorityName, options =>
            {
                options.ClientId = configuration["OIDC_CLIENT_ID"];
                var clientSecret = configuration["OIDC_CLIENT_SECRET"];
                if (!string.IsNullOrWhiteSpace(clientSecret))
                {
                    options.ClientSecret = clientSecret;
                }

                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SignedOutCallbackPath = "/signout-callback-oidc";
                options.SignedOutRedirectUri = ConsoleRoutePaths.Splash();
                //options.MapInboundClaims = false;
                options.SaveTokens = true;
                //options.GetClaimsFromUserInfoEndpoint = false;
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("offline_access");
                //options.ClaimActions.Clear();
                options.ClaimActions.MapUniqueJsonKey("preferred_username", "preferred_username");
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = "preferred_username"
                };
                options.Events = new OpenIdConnectEvents
                {
                    OnRedirectToIdentityProvider = context =>
                    {
                        var loginProviders = context.HttpContext.RequestServices.GetRequiredService<WebConsoleLoginProviderCatalog>();
                        var challengeValue = context.Properties?.Items.TryGetValue(WebConsoleLoginProviderCatalog.AuthenticationChallengeValueItemKey, out var challengeItem) == true
                            ? challengeItem
                            : null;
                        var owner = context.Properties?.Items.TryGetValue(WebConsoleLoginProviderCatalog.AuthenticationOwnerItemKey, out var ownerItem) == true
                            ? ownerItem
                            : null;
                        var providerId = context.Properties?.Items.TryGetValue(WebConsoleLoginProviderCatalog.AuthenticationProviderItemKey, out var providerItem) == true
                            ? providerItem
                            : null;
                        var selectedProvider = string.IsNullOrWhiteSpace(challengeValue)
                            ? loginProviders.ResolveProvider(providerId, owner)
                            : null;
                        context.ProtocolMessage.SetParameter(
                            loginProviders.ChallengeParameterName,
                            challengeValue ?? selectedProvider!.ChallengeValue);

                        return Task.CompletedTask;
                    },
                    OnTicketReceived = context =>
                    {
                        var idToken = context.Properties?.GetTokenValue("id_token");
                        var accessToken = context.Properties?.GetTokenValue("access_token");
                        if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(accessToken))
                        {
                            return Task.CompletedTask;
                        }

                        List<AuthenticationToken> storedTokens =
                        [
                            new() { Name = "access_token", Value = accessToken },
                            new() { Name = "id_token", Value = idToken }
                        ];

                        var expiresAt = context.Properties?.GetTokenValue("expires_at");
                        if (!string.IsNullOrWhiteSpace(expiresAt))
                        {
                            storedTokens.Add(new AuthenticationToken { Name = "expires_at", Value = expiresAt });
                        }

                        // Keep the Web Console cookie lean: Dex still receives and persists the
                        // refresh token for its own offline session/logout handling, but Makana
                        // only needs the access/id tokens in the local auth ticket.
                        context.Properties?.StoreTokens(storedTokens);
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure(static options =>
            {
                options.LoginPath = ConsoleRoutePaths.Login();
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
                options.SlidingExpiration = true;
                options.Events = new CookieAuthenticationEvents
                {
                    OnValidatePrincipal = context =>
                    {
                        var expiresAt = context.Properties?.GetTokenValue("expires_at");
                        if (!DateTimeOffset.TryParse(
                                expiresAt,
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.RoundtripKind,
                                out var tokenExpiration) ||
                            tokenExpiration > DateTimeOffset.UtcNow.AddMinutes(ExpirationSkewMinutes))
                        {
                            return Task.CompletedTask;
                        }

                        context.HttpContext.Items[ReauthenticationRequiredItemKey] = bool.TrueString;
                        context.RejectPrincipal();
                        return context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    },
                    OnRedirectToLogin = context =>
                    {
                        var redirectTarget = BuildLoginRedirectUri(context);
                        if (!context.Request.IsHtmx())
                        {
                            context.Response.Redirect(redirectTarget);
                            return Task.CompletedTask;
                        }

                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        context.Response.Headers.Append("Vary", HtmxRequestHeaders.Keys.Request);
                        context.Response.Htmx(h => h.Redirect(redirectTarget));
                        return Task.CompletedTask;

                        static string BuildLoginRedirectUri(RedirectContext<CookieAuthenticationOptions> context)
                        {
                            var returnUrl = context.Request.PathBase.Add(context.Request.Path) + context.Request.QueryString;
                            var owner = context.Request.RouteValues.TryGetValue("owner", out var ownerValue)
                                ? ownerValue?.ToString()
                                : null;
                            Dictionary<string, string?> query = new(StringComparer.Ordinal)
                            {
                                ["returnUrl"] = returnUrl
                            };
                            if (!string.IsNullOrWhiteSpace(owner))
                            {
                                query["owner"] = owner;
                            }

                            var path = context.HttpContext.Items.ContainsKey(ReauthenticationRequiredItemKey)
                                ? ConsoleRoutePaths.Reauthenticate()
                                : ConsoleRoutePaths.Login();

                            return QueryHelpers.AddQueryString(path, query);
                        }
                    }
                };
            });

        return services;
    }
}
