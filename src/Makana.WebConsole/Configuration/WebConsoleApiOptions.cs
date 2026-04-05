namespace Makana.WebConsole.Configuration;

public sealed class WebConsoleApiOptions
{
    public string? BrowserBaseUrl { get; set; }

    public string? ApiToken { get; set; }

    public string? PublicApiUrl { get; set; }

    public string? PublicWebConsoleUrl { get; set; }

    public string? PublicWebConsoleLoginUrl { get; set; }
}
