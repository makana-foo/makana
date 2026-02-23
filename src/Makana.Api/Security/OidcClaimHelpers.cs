using System.Security.Claims;

namespace Makana.Api.Security;

internal static class OidcClaimHelpers
{
    public static string? ResolveCanonicalUserId(ClaimsPrincipal principal, string providerKey)
    {
        var subject = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        var issuer = principal.FindFirst("iss")?.Value;
        if (!string.IsNullOrWhiteSpace(issuer))
        {
            return $"oidc:{issuer}:{subject}";
        }

        return $"{providerKey}:{subject}";
    }
}
