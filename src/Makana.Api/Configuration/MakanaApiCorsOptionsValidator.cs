using Microsoft.Extensions.Options;

namespace Makana.Api.Configuration;

internal sealed class MakanaApiCorsOptionsValidator : IValidateOptions<MakanaApiCorsOptions>
{
    public ValidateOptionsResult Validate(string? name, MakanaApiCorsOptions options)
    {
        List<string> failures = [];

        foreach (var origin in options.AllowedOrigins ?? [])
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                failures.Add("Cors:AllowedOrigins entries must not be empty.");
                continue;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(uri.Query) ||
                !string.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath is not ("" or "/"))
            {
                failures.Add($"Cors:AllowedOrigins entry '{origin}' must be an absolute origin without path, query, or fragment.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
