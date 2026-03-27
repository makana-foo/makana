using System.Security.Claims;
using Makana.Api.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Makana.Api.Security;

internal sealed record ValidatedOidcSubjectToken(
    ClaimsPrincipal Principal,
    string Issuer,
    string Subject,
    DateTimeOffset ExpiresAt);

internal sealed class OidcSubjectTokenValidator : IDisposable
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JsonWebTokenHandler _tokenHandler = new();
    private readonly OidcSubjectTokenValidationOptions _options;
    private readonly ILogger<OidcSubjectTokenValidator> _logger;

    public OidcSubjectTokenValidator(
        IHttpClientFactory httpClientFactory,
        IOptions<OidcSubjectTokenValidationOptions> options,
        ILogger<OidcSubjectTokenValidator> logger)
    {
        _options = options.Value;
        _logger = logger;

        var retriever = new HttpDocumentRetriever(httpClientFactory.CreateClient(nameof(OidcSubjectTokenValidator)))
        {
            RequireHttps = _options.MetadataAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        };

        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            _options.MetadataAddress,
            new OpenIdConnectConfigurationRetriever(),
            retriever);
    }

    public async Task<ValidatedOidcSubjectToken?> ValidateAsync(string subjectToken, CancellationToken cancellationToken)
    {
        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load OIDC metadata for subject-token validation.");
            return null;
        }

        var validationParameters = new TokenValidationParameters
        {
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateIssuer = true,
            ValidIssuers =
            [
                configuration.Issuer,
                _options.Authority
            ],
            ValidateAudience = true,
            ValidAudience = _options.SubjectTokenAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(_options.ClockSkewSeconds),
            NameClaimType = PulumiAuthenticationDefaults.LoginClaim
        };

        TokenValidationResult validationResult;
        try
        {
            validationResult = await _tokenHandler.ValidateTokenAsync(subjectToken, validationParameters);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subject-token validation threw unexpectedly.");
            return null;
        }

        if (!validationResult.IsValid || validationResult.ClaimsIdentity is null)
        {
            _logger.LogWarning(
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
            _logger.LogWarning("Validated subject token did not include both iss and sub claims.");
            return null;
        }

        var expiresAt = validationResult.SecurityToken?.ValidTo is DateTime validTo && validTo != DateTime.MinValue
            ? new DateTimeOffset(DateTime.SpecifyKind(validTo, DateTimeKind.Utc))
            : DateTimeOffset.UtcNow;

        return new ValidatedOidcSubjectToken(principal, issuer, subject, expiresAt);
    }

    public void Dispose()
    {
        _configurationManager.RequestRefresh();
    }
}
