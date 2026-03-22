using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Makana.WebConsole.Endpoints;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Makana.WebConsole.Services;
using System.Globalization;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

static bool IsLiveUpdateHtmxRequest(HttpRequest request)
{
    return string.Equals(request.Query["handler"], "LiveUpdateDelta", StringComparison.Ordinal) &&
           string.Equals(request.Headers["HX-Request"], "true", StringComparison.OrdinalIgnoreCase);
}

builder.AddServiceDefaults();

builder.Services.AddHttpClient<PulumiApiClient>((sp, client) =>
{
    PulumiApiClient.ConfigureHttpClient(client, sp.GetRequiredService<IConfiguration>());
}).AddHttpMessageHandler<PulumiApiAuthHeaderHandler>();
builder.Services.AddHttpClient<PulumiCliLoginService>((sp, client) =>
{
    PulumiCliLoginService.ConfigureHttpClient(client, sp.GetRequiredService<IConfiguration>());
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
        options.LoginPath = "/Login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (!IsLiveUpdateHtmxRequest(context.Request))
            {
                context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["X-Makana-Reauth-Redirect"] = context.RedirectUri;
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = async context =>
        {
            var cookieExpiresUtc = context.Properties?.ExpiresUtc;
            var tokenExpiresUtc = ResolveTokenExpiresUtc(context.Properties);
            var effectiveExpiresUtc = tokenExpiresUtc is null
                ? cookieExpiresUtc
                : cookieExpiresUtc is null
                    ? tokenExpiresUtc
                    : tokenExpiresUtc < cookieExpiresUtc
                        ? tokenExpiresUtc
                        : cookieExpiresUtc;

            if (effectiveExpiresUtc is not null && effectiveExpiresUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return;
            }

            if (effectiveExpiresUtc is not null)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
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

static DateTimeOffset? ResolveTokenExpiresUtc(AuthenticationProperties? properties)
{
    var expiresAt = properties?.GetTokenValue("expires_at");
    if (!string.IsNullOrWhiteSpace(expiresAt) &&
        DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedExpiresAt))
    {
        return parsedExpiresAt;
    }

    return TryReadJwtExpiration(properties?.GetTokenValue("access_token")) ??
           TryReadJwtExpiration(properties?.GetTokenValue("id_token"));
}

static DateTimeOffset? TryReadJwtExpiration(string? token)
{
    if (string.IsNullOrWhiteSpace(token))
    {
        return null;
    }

    var segments = token.Split('.');
    if (segments.Length < 2)
    {
        return null;
    }

    try
    {
        var payloadBytes = DecodeBase64Url(segments[1]);
        using var json = JsonDocument.Parse(payloadBytes);
        if (!json.RootElement.TryGetProperty("exp", out var expElement))
        {
            return null;
        }

        long expSeconds;
        if (expElement.ValueKind == JsonValueKind.Number && expElement.TryGetInt64(out var numericExp))
        {
            expSeconds = numericExp;
        }
        else if (expElement.ValueKind == JsonValueKind.String &&
                 long.TryParse(expElement.GetString(), CultureInfo.InvariantCulture, out var stringExp))
        {
            expSeconds = stringExp;
        }
        else
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(expSeconds);
    }
    catch
    {
        return null;
    }
}

static byte[] DecodeBase64Url(string value)
{
    var builder = new StringBuilder(value.Replace('-', '+').Replace('_', '/'));
    while (builder.Length % 4 != 0)
    {
        builder.Append('=');
    }

    return Convert.FromBase64String(builder.ToString());
}

static bool ShouldAllowInsecureOidcBackchannel(IHostEnvironment environment, string authority)
{
    if (environment.IsProduction())
    {
        return false;
    }

    if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri))
    {
        return false;
    }

    return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
           uri.Host.EndsWith(".dev.localhost", StringComparison.OrdinalIgnoreCase);
}

builder.Services.Configure<RouteOptions>(builder.Configuration.GetSection(nameof(RouteOptions)));

builder.Services.AddAuthorization();
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Error");

    // Secondary aliases live in conventions; canonical owner-scoped routes are declared directly on
    // the pages.
    options.Conventions.AddPageRoute("/Index", "/");
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
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapTestAuthEndpoints();
}

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
