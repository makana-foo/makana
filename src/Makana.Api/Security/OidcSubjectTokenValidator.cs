using System.Security.Claims;
using Makana.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace Makana.Api.Security;

internal sealed record ValidatedOidcSubjectToken(
    ClaimsPrincipal Principal,
    string Issuer,
    string Subject,
    DateTimeOffset ExpiresAt);

internal sealed class OidcSubjectTokenValidator(
    IOidcTokenValidationService tokenValidationService,
    ILogger<OidcSubjectTokenValidator> logger)
{
    public async Task<ValidatedOidcSubjectToken?> ValidateAsync(string subjectToken, CancellationToken cancellationToken)
    {
        TokenValidationResult validationResult;
        try
        {
            validationResult = await tokenValidationService.ValidateTokenAsync(
                MakanaOidcDefaults.DefaultTokenValidationName,
                subjectToken,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Subject-token validation threw unexpectedly.");
            return null;
        }

        if (!validationResult.IsValid || validationResult.ClaimsIdentity is null)
        {
            logger.LogWarning(
                validationResult.Exception,
                "Subject-token validation failed with {FailureType}.",
                validationResult.Exception?.GetType().Name ?? "unknown-error");
            return null;
        }

        var principal = new ClaimsPrincipal(validationResult.ClaimsIdentity);
        var issuer = principal.FindFirstValue("iss");
        var subject = principal.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            logger.LogWarning("Validated subject token did not include both iss and sub claims.");
            return null;
        }

        var expiresAt = validationResult.SecurityToken?.ValidTo is DateTime validTo && validTo != DateTime.MinValue
            ? new DateTimeOffset(DateTime.SpecifyKind(validTo, DateTimeKind.Utc))
            : DateTimeOffset.UtcNow;

        return new ValidatedOidcSubjectToken(principal, issuer, subject, expiresAt);
    }
}
