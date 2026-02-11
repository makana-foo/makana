using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Makana.WebConsole.Endpoints;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Makana.WebConsole.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddTransient<PulumiApiAuthHeaderHandler>();
builder.Services.AddScoped<ConsoleContextService>();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/Login";
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        var casdoorUrl = builder.Configuration["CASDOOR_URL"]
            ?? throw new InvalidOperationException("CASDOOR_URL was not configured. Ensure Aspire AppHost injects Casdoor settings.");
        var casdoorClientId = builder.Configuration["CASDOOR_CLIENT_ID"]
            ?? throw new InvalidOperationException("CASDOOR_CLIENT_ID was not configured.");
        var casdoorClientSecret = builder.Configuration["CASDOOR_CLIENT_SECRET"]
            ?? throw new InvalidOperationException("CASDOOR_CLIENT_SECRET was not configured.");

        options.Authority = casdoorUrl.TrimEnd('/');
        options.RequireHttpsMetadata = false;
        options.MetadataAddress = $"{options.Authority}/.well-known/openid-configuration";
        options.ClientId = casdoorClientId;
        options.ClientSecret = casdoorClientSecret;
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

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
