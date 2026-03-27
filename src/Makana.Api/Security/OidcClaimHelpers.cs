using System.Security.Claims;
using System.Text;

namespace Makana.Api.Security;

internal static class OidcClaimHelpers
{
    public static string? ResolveCanonicalUserId(ClaimsPrincipal principal)
    {
        return principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
    }

    public static string? ResolveLogin(ClaimsPrincipal principal)
    {
        return GetClaim(
            principal,
            PulumiAuthenticationDefaults.LoginClaim,
            "username",
            "login",
            "nickname",
            ClaimTypes.Upn,
            ClaimTypes.Name)
            ?? (ResolveEmailVerified(principal) ? ResolveEmail(principal) : null);
    }

    public static string? ResolveDisplayName(ClaimsPrincipal principal)
    {
        return GetClaim(
            principal,
            ClaimTypes.Name,
            "name",
            PulumiAuthenticationDefaults.LoginClaim,
            "nickname");
    }

    public static string? ResolveEmail(ClaimsPrincipal principal)
        => principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");

    public static bool ResolveEmailVerified(ClaimsPrincipal principal)
    {
        var rawValue = principal.FindFirstValue("email_verified");
        return string.Equals(rawValue, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawValue, "1", StringComparison.Ordinal);
    }

    private static string? GetClaim(ClaimsPrincipal principal, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = principal.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
