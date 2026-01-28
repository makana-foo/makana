using Makana.WebConsole.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient<PulumiApiClient>((sp, client) =>
{
    PulumiApiClient.ConfigureHttpClient(client, sp.GetRequiredService<IConfiguration>());
});
builder.Services.AddRazorPages();

var app = builder.Build();

app.MapDefaultEndpoints();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();

app.Run();
