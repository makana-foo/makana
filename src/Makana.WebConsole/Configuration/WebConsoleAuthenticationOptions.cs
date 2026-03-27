using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Makana.WebConsole.Configuration;

public sealed class WebConsoleAuthenticationOptions
{
    public const string SectionName = "Makana:Authentication";

    public Dictionary<string, OpenIdConnectOptions> Providers { get; set; } = [];
    public Dictionary<string, WebConsoleOrganizationAuthenticationOptions> Organizations { get; set; } = [];
}

public sealed class WebConsoleOrganizationAuthenticationOptions
{
    public List<string> Providers { get; set; } = [];
    public bool AllowGlobalProviders { get; set; }
}
