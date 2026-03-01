using System.Security.Claims;
using System.Text;

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

        return $"{providerKey}:{subject}";
    }

    public static string? ResolveLogin(ClaimsPrincipal principal)
    {
        var asserted = GetClaim(
            principal,
            "preferred_username",
            "username",
            "login",
            "nickname",
            ClaimTypes.Upn,
            ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(asserted))
        {
            return null;
        }

        return SanitizeLogin(asserted);
    }

    private static string? GetClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var claim = principal.FindFirst(claimType)?.Value;
            if (!string.IsNullOrWhiteSpace(claim))
            {
                return claim;
            }
        }

        return null;
    }

    private static string SanitizeLogin(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_')
            {
                builder.Append(ch);
            }
            else
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "user" : result;
    }
}
