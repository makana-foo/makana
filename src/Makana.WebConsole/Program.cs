using Makana.WebConsole.DependencyInjection;
using Makana.WebConsole.Endpoints;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.AddServiceDefaults();
builder.Services.AddMakanaWebConsoleApplication(builder.Configuration);

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
