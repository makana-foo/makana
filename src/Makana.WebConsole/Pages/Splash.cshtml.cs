using System.Text;
using Makana.WebConsole.Configuration;
using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Makana.WebConsole.Pages;

[AllowAnonymous]
public sealed class SplashModel(
    IOptions<WebConsoleApiOptions> apiOptions,
    ConsoleContextService consoleContextService) : PageModel
{
    private readonly WebConsoleApiOptions _apiOptions = apiOptions.Value;

    public string MakanaBackendUrl { get; private set; } = string.Empty;

    public string MakanaConsoleUrl { get; private set; } = string.Empty;

    public string MakanaConsoleDomain { get; private set; } = string.Empty;

    public bool IsAuthenticated { get; private set; }

    public string DashboardUrl { get; private set; } = string.Empty;

    public string MakanaLoginCommand { get; private set; } = string.Empty;

    public string PulumiLoginCommand { get; } =
        """
        $env:PULUMI_BACKEND_URL=""
        $env:PULUMI_CONSOLE_DOMAIN=""
        pulumi login
        """;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        MakanaBackendUrl = NormalizePublicUrl(_apiOptions.PublicApiUrl);
        MakanaConsoleUrl = NormalizePublicUrl(_apiOptions.PublicWebConsoleUrl ?? GetRequestOrigin());
        MakanaConsoleDomain = ExtractConsoleDomain(
            _apiOptions.PublicWebConsoleLoginUrl ?? _apiOptions.PublicWebConsoleUrl ?? GetRequestOrigin());
        MakanaLoginCommand = BuildMakanaLoginCommand(MakanaBackendUrl, MakanaConsoleDomain);
        IsAuthenticated = User.Identity?.IsAuthenticated == true;

        if (!IsAuthenticated)
        {
            return;
        }

        var context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        DashboardUrl = ConsoleRoutePaths.OwnerDashboard(context.SelectedOwner);
    }

    private string GetRequestOrigin()
    {
        return $"{Request.Scheme}://{Request.Host}";
    }

    private static string NormalizePublicUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value ?? string.Empty;
        }

        if ((uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443) ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.Port == 80))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return uri.PathAndQuery == "/" && string.IsNullOrEmpty(uri.Fragment)
            ? uri.GetLeftPart(UriPartial.Authority)
            : uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    private static string BuildMakanaLoginCommand(string backendUrl, string consoleUrl)
    {
        var builder = new StringBuilder();
        builder.Append("$env:PULUMI_BACKEND_URL=\"");
        builder.Append(backendUrl);
        builder.AppendLine("\"");
        builder.Append("$env:PULUMI_CONSOLE_DOMAIN=\"");
        builder.Append(consoleUrl);
        builder.AppendLine("\"");
        builder.Append("pulumi login");
        return builder.ToString();
    }

    private static string ExtractConsoleDomain(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value;
        }

        return uri.IsDefaultPort ? uri.Host : uri.Authority;
    }
}
