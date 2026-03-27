using Microsoft.Extensions.Options;

namespace Makana.WebConsole.Configuration;

internal sealed class WebConsoleApiOptionsValidator : IValidateOptions<WebConsoleApiOptions>
{
    public ValidateOptionsResult Validate(string? name, WebConsoleApiOptions options)
    {
        List<string> failures = [];

        ValidateOptionalAbsoluteUri(options.ServerBaseUrl, "API_URL", failures);
        ValidateOptionalHttpOrHttpsUri(options.BrowserBaseUrl, "API_HTTPS", failures);
        ValidateOptionalHttpOrHttpsUri(options.PublicApiUrl, "Makana:ApiPublicUrl", failures);
        ValidateOptionalHttpOrHttpsUri(options.PublicWebConsoleUrl, "Makana:WebConsolePublicUrl", failures);
        ValidateOptionalHttpOrHttpsUri(options.PublicWebConsoleLoginUrl, "Makana:WebConsoleLoginUrl", failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateOptionalAbsoluteUri(string? value, string key, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            failures.Add($"{key} must be an absolute URI.");
        }
    }

    private static void ValidateOptionalHttpOrHttpsUri(string? value, string key, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add($"{key} must be an absolute http or https URI.");
        }
    }
}
