using Microsoft.IdentityModel.Tokens;

namespace Makana.Authentication;

public interface IOidcTokenValidationService
{
    Task<TokenValidationResult> ValidateTokenAsync(string validationName, string token, CancellationToken cancellationToken);
}
