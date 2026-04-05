using Makana.WebConsole.DependencyInjection;
using Makana.WebConsole.Endpoints;
using Makana.WebConsole.Services;
using Htmx;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

// SECURITY: Disable the 'Server' header to prevent fingerprinting
// CMMC SI.L2-3.14.1 - Hide system information
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;

    // SECURITY: Limit request body size to prevent DoS attacks with oversized payloads
    // Default is ~28.6MB, we limit to 1MB which is generous for JSON API requests
    serverOptions.Limits.MaxRequestBodySize = 1 * 1024 * 1024; // 1MB
});

builder.WebHost.UseStaticWebAssets();

builder.AddServiceDefaults();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddMakanaWebConsoleApplication(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health") ||
        context.Request.Path.StartsWithSegments("/alive"))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.ContainsKey("X-Makana-Ingress"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var redirectUri = await context.RequestServices
        .GetRequiredService<OrganizationSsoReauthenticationService>()
        .GetRedirectUriAsync(context, context.RequestAborted);
    if (string.IsNullOrWhiteSpace(redirectUri))
    {
        await next();
        return;
    }

    if (!context.Request.IsHtmx())
    {
        context.Response.Redirect(redirectUri);
        return;
    }

    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    context.Response.Headers.Append("Vary", HtmxRequestHeaders.Keys.Request);
    context.Response.Htmx(h => h.Redirect(redirectUri));
});
app.UseAuthorization();

app.MapCliLoginEndpoints();
app.MapAvatarEndpoints();
app.MapUserPreferenceEndpoints();
app.MapStackLiveUpdateEndpoints();
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapTestAuthEndpoints();
}

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
