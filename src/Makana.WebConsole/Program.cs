using Htmx;
using Makana.Compatibility;
using Makana.WebConsole.Endpoints;
using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using System.Globalization;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

const string ReauthenticationRequiredItemKey = "makana.auth.reauthenticate";
const int ExpirationSkewMinutes = 1;

builder.AddServiceDefaults();

builder.Services.AddHttpClient<PulumiApiClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new("https+http://api");
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(PulumiHeaders.Accept));
})
    .AddHttpMessageHandler<PulumiApiAuthHeaderHandler>();
builder.Services.AddHttpClient<PulumiCliLoginService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    client.BaseAddress = new("https+http://api");
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(PulumiHeaders.Accept));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddTransient<PulumiApiAuthHeaderHandler>();
builder.Services.AddScoped<ConsoleContextService>();
builder.Services.AddSingleton<UserAvatarService>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/login";
        options.Events.OnValidatePrincipal = async context =>
        {
            var expiresAt = context.Properties?.GetTokenValue("expires_at");
            if (!DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var tokenExpiration) ||
                tokenExpiration > DateTimeOffset.UtcNow.AddMinutes(ExpirationSkewMinutes))
            {
                return;
            }

            context.HttpContext.Items[ReauthenticationRequiredItemKey] = bool.TrueString;
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        };
        options.Events.OnRedirectToLogin = context =>
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
        };
        options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        options.SlidingExpiration = true;
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        var oidcAuthority = builder.Configuration["OIDC_AUTHORITY"]
            ?? throw new InvalidOperationException("OIDC_AUTHORITY was not configured.");
        var oidcClientId = builder.Configuration["OIDC_CLIENT_ID"]
            ?? throw new InvalidOperationException("OIDC_CLIENT_ID was not configured.");
        var oidcClientSecret = builder.Configuration["OIDC_CLIENT_SECRET"]
            ?? throw new InvalidOperationException("OIDC_CLIENT_SECRET was not configured.");
        var oidcMetadataAddress = builder.Configuration["OIDC_METADATA_ADDRESS"];

        options.Authority = oidcAuthority.TrimEnd('/');
        options.RequireHttpsMetadata = false;
        options.MetadataAddress = string.IsNullOrWhiteSpace(oidcMetadataAddress)
            ? $"{options.Authority}/.well-known/openid-configuration"
            : oidcMetadataAddress;
        options.ClientId = oidcClientId;
        options.ClientSecret = oidcClientSecret;
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.MapInboundClaims = false;
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.ClaimActions.Clear();
        options.ClaimActions.MapUniqueJsonKey("preferred_username", "preferred_username");
        options.TokenValidationParameters = new TokenValidationParameters
        {
            NameClaimType = "preferred_username"
        };
        options.Events.OnRedirectToIdentityProvider = context =>
        {
            if (context.Properties?.Items.TryGetValue(WebConsoleLoginProvider.AuthenticationProviderItemKey, out var providerId) == true &&
                !string.IsNullOrWhiteSpace(providerId))
            {
                var selectedProvider = WebConsoleLoginProvider.GetOrDefault(providerId);
                context.ProtocolMessage.SetParameter(
                    WebConsoleLoginProvider.ChallengeProviderParameterName,
                    selectedProvider.Id);
            }

            return Task.CompletedTask;
        };
        options.Events.OnTicketReceived = context =>
        {
            var idToken = context.Properties?.GetTokenValue("id_token");
            var accessToken = context.Properties?.GetTokenValue("access_token");
            if (string.IsNullOrWhiteSpace(idToken) || string.IsNullOrWhiteSpace(accessToken))
            {
                return Task.CompletedTask;
            }

            var storedTokens = new List<AuthenticationToken>
            {
                // Keep only the real downstream tokens the Web Console uses for API calls and
                // CLI token exchange so the auth ticket stays as small as possible.
                new() { Name = "access_token", Value = accessToken },
                new() { Name = "id_token", Value = idToken }
            };

            var expiresAt = context.Properties?.GetTokenValue("expires_at");
            if (!string.IsNullOrWhiteSpace(expiresAt))
            {
                storedTokens.Add(new AuthenticationToken { Name = "expires_at", Value = expiresAt });
            }

            context.Properties?.StoreTokens(storedTokens);
            return Task.CompletedTask;
        };

        if (ShouldAllowInsecureOidcBackchannel(builder.Environment, oidcAuthority))
        {
            // Local AppHost OIDC authorities use a self-signed proxy certificate that the
            // browser can ignore, but the server-side OIDC backchannel still must complete
            // token and userinfo requests for the sign-in callback.
            options.BackchannelHttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
        }
    });

static bool ShouldAllowInsecureOidcBackchannel(IHostEnvironment environment, string authority)
{
    if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
    {
        return true;
    }

    return false;
}

static string BuildLoginRedirectUri(RedirectContext<CookieAuthenticationOptions> context)
{
    if (!context.HttpContext.Items.ContainsKey(ReauthenticationRequiredItemKey))
    {
        return context.RedirectUri;
    }

    var returnUrl = context.Request.PathBase.Add(context.Request.Path).ToString() + context.Request.QueryString;
    return QueryHelpers.AddQueryString("/reauthenticate", "returnUrl", returnUrl);
}

builder.Services.Configure<RouteOptions>(builder.Configuration.GetSection(nameof(RouteOptions)));

builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
    {
        options.Conventions.AuthorizeFolder("/");
        options.Conventions.AllowAnonymousToPage("/Splash");
        options.Conventions.AllowAnonymousToPage("/Login");
        options.Conventions.AllowAnonymousToPage("/Reauthenticate");
        options.Conventions.AllowAnonymousToPage("/Error");

        // Secondary aliases live in conventions; canonical owner-scoped routes are declared directly on
        // the pages.
        options.Conventions.AddPageRoute("/Stacks/Details", "/{owner}/stacks/{project}/{stack}/{runType}/{updateRef}");
    });

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapCliLoginEndpoints();
app.MapAvatarEndpoints();
app.MapUserPreferenceEndpoints();
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapTestAuthEndpoints();
}

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
