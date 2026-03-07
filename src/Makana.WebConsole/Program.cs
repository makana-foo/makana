using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Makana.WebConsole.Endpoints;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Makana.WebConsole.Services;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

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
            var expiresAt = context.Properties?.GetTokenValue("expires_at");
            if (string.IsNullOrWhiteSpace(expiresAt))
            {
                return;
            }

            if (!DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresUtc))
            {
                return;
            }

            if (expiresUtc > DateTimeOffset.UtcNow.AddMinutes(1))
            {
                return;
            }

            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
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
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
    });

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
if (app.Environment.IsDevelopment())
{
    app.MapTestAuthEndpoints();
}

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
