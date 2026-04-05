using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Makana.Authentication;

internal sealed class MakanaOidcAuthorityOptionsValidator : IValidateOptions<MakanaOidcAuthorityOptions>
{
    public ValidateOptionsResult Validate(string? name, MakanaOidcAuthorityOptions options)
    {
        List<string> failures = [];

        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authority) ||
            (authority.Scheme != Uri.UriSchemeHttp && authority.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("Authority must be an absolute http or https URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.MetadataAddress) &&
            (!Uri.TryCreate(options.MetadataAddress, UriKind.Absolute, out var metadataAddress) ||
             (metadataAddress.Scheme != Uri.UriSchemeHttp && metadataAddress.Scheme != Uri.UriSchemeHttps)))
        {
            failures.Add("MetadataAddress must be an absolute http or https URL when provided.");
        }

        if (!string.IsNullOrWhiteSpace(options.BackchannelBaseUrl) &&
            (!Uri.TryCreate(options.BackchannelBaseUrl, UriKind.Absolute, out var backchannelBaseUri) ||
             (backchannelBaseUri.Scheme != Uri.UriSchemeHttp && backchannelBaseUri.Scheme != Uri.UriSchemeHttps)))
        {
            failures.Add("BackchannelBaseUrl must be an absolute http or https URL when provided.");
        }

        if (options.StartupWarmupTimeout <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(MakanaOidcAuthorityOptions.StartupWarmupTimeout)} must be greater than zero.");
        }

        if (options.BackgroundRefreshInterval <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(MakanaOidcAuthorityOptions.BackgroundRefreshInterval)} must be greater than zero.");
        }

        if (options.AutomaticRefreshInterval < ConfigurationManager<OpenIdConnectConfiguration>.MinimumAutomaticRefreshInterval)
        {
            failures.Add($"{nameof(MakanaOidcAuthorityOptions.AutomaticRefreshInterval)} must be at least {ConfigurationManager<OpenIdConnectConfiguration>.MinimumAutomaticRefreshInterval}.");
        }

        if (options.HotPathRefreshMinimumInterval < ConfigurationManager<OpenIdConnectConfiguration>.MinimumRefreshInterval)
        {
            failures.Add($"{nameof(MakanaOidcAuthorityOptions.HotPathRefreshMinimumInterval)} must be at least {ConfigurationManager<OpenIdConnectConfiguration>.MinimumRefreshInterval}.");
        }

        if (options.LastKnownGoodLifetime <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(MakanaOidcAuthorityOptions.LastKnownGoodLifetime)} must be greater than zero.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

internal sealed class MakanaOidcTokenValidationOptionsValidator : IValidateOptions<MakanaOidcTokenValidationOptions>
{
    public ValidateOptionsResult Validate(string? name, MakanaOidcTokenValidationOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.AuthorityName))
        {
            failures.Add("AuthorityName is required.");
        }

        if (options.ValidAudiences.Count == 0 || options.ValidAudiences.All(string.IsNullOrWhiteSpace))
        {
            failures.Add("At least one valid audience is required.");
        }

        if (options.ClockSkew < TimeSpan.Zero)
        {
            failures.Add($"{nameof(MakanaOidcTokenValidationOptions.ClockSkew)} must be zero or greater.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
