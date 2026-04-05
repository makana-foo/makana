using Microsoft.Extensions.Options;

namespace Makana.WebConsole.Configuration;

internal sealed class WebConsoleApiOptionsValidator : IValidateOptions<WebConsoleApiOptions>
{
    public ValidateOptionsResult Validate(string? name, WebConsoleApiOptions options)
    {
        List<string> failures = [];

        ValidateOptionalHttpsUri(options.BrowserBaseUrl, "API_HTTPS", failures);
        ValidateOptionalHttpsUri(options.PublicApiUrl, "Makana:ApiPublicUrl", failures);
        ValidateOptionalHttpsUri(options.PublicWebConsoleUrl, "Makana:WebConsolePublicUrl", failures);
        ValidateOptionalHttpsUri(options.PublicWebConsoleLoginUrl, "Makana:WebConsoleLoginUrl", failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateOptionalHttpsUri(string? value, string key, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{key} must be an absolute https URI.");
        }
    }
}
