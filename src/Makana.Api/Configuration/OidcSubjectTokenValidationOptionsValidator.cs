using Microsoft.Extensions.Options;

namespace Makana.Api.Configuration;

internal sealed class OidcSubjectTokenValidationOptionsValidator : IValidateOptions<OidcSubjectTokenValidationOptions>
{
    public ValidateOptionsResult Validate(string? name, OidcSubjectTokenValidationOptions options)
    {
        List<string> failures = [];

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) ||
            (authority.Scheme != Uri.UriSchemeHttp && authority.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("OIDC_AUTHORITY must be an absolute http or https URL.");
        }

        if (!Uri.TryCreate(options.MetadataAddress, UriKind.Absolute, out var metadataAddress) ||
            (metadataAddress.Scheme != Uri.UriSchemeHttp && metadataAddress.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("OIDC_METADATA_ADDRESS must be an absolute http or https URL.");
        }

        if (string.IsNullOrWhiteSpace(options.SubjectTokenAudience))
        {
            failures.Add("OIDC_SUBJECT_TOKEN_AUDIENCE is required.");
        }

        if (options.MaxAccessTokenLifetimeSeconds <= 0)
        {
            failures.Add($"{nameof(OidcSubjectTokenValidationOptions.MaxAccessTokenLifetimeSeconds)} must be greater than zero.");
        }

        if (options.ClockSkewSeconds < 0)
        {
            failures.Add($"{nameof(OidcSubjectTokenValidationOptions.ClockSkewSeconds)} must be zero or greater.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
