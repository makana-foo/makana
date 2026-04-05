using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Makana.Authentication;

internal sealed class OidcTokenValidationService(
    OidcAuthorityRegistry authorityRegistry,
    IOptionsMonitor<MakanaOidcTokenValidationOptions> validationOptionsMonitor,
    ILogger<OidcTokenValidationService> logger) : IOidcTokenValidationService
{
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public async Task<TokenValidationResult> ValidateTokenAsync(
        string validationName,
        string token,
        CancellationToken cancellationToken)
    {
        var validationOptions = validationOptionsMonitor.Get(validationName);
        var authority = authorityRegistry.GetAuthority(validationOptions.AuthorityName);
        var validationParameters = BuildValidationParameters(authority, validationOptions);

        var result = await _tokenHandler.ValidateTokenAsync(token, validationParameters);
        if (!result.IsValid && ShouldRetryWithRefreshedConfiguration(result.Exception))
        {
            logger.LogInformation(
                result.Exception,
                "Refreshing OIDC metadata for validation profile {ValidationName} after signing-key validation failure.",
                validationName);

            authority.ConfigurationManager.RequestRefresh();
            validationParameters.RefreshBeforeValidation = true;
            result = await _tokenHandler.ValidateTokenAsync(token, validationParameters);
        }

        return result;
    }

    private static TokenValidationParameters BuildValidationParameters(
        OidcAuthorityRuntime authority,
        MakanaOidcTokenValidationOptions validationOptions)
    {
        var validIssuers = validationOptions.ValidIssuers
            .Concat(
            [
                authority.Options.Authority,
                authority.Options.Authority + "/"
            ])
            .Where(static issuer => !string.IsNullOrWhiteSpace(issuer))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidIssuers = validIssuers,
            ConfigurationManager = authority.ConfigurationManager,
            ValidateAudience = true,
            ValidAudiences = validationOptions.ValidAudiences.Where(static audience => !string.IsNullOrWhiteSpace(audience)).ToArray(),
            ValidateLifetime = true,
            ClockSkew = validationOptions.ClockSkew,
            NameClaimType = validationOptions.NameClaimType
        };
    }

    private static bool ShouldRetryWithRefreshedConfiguration(Exception? exception)
        => exception is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException;
}
