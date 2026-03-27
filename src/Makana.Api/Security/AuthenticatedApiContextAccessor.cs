using System.Security.Claims;

namespace Makana.Api.Security;

internal sealed record AuthenticatedApiContext(
    ClaimsPrincipal Principal,
    bool IsAuthenticated,
    string? UserId,
    string? Login,
    string? OrgId,
    string? TeamId,
    string? ApiToken,
    string? UpdateToken,
    string? TokenType,
    string? PrincipalType,
    string? PrincipalId,
    bool HasAdminScope);

internal sealed class AuthenticatedApiContextAccessor(IHttpContextAccessor httpContextAccessor)
{
    public AuthenticatedApiContext GetCurrent()
        => Resolve(httpContextAccessor.HttpContext?.User);

    public AuthenticatedApiContext Resolve(ClaimsPrincipal? principal)
    {
        principal ??= new ClaimsPrincipal(new ClaimsIdentity());

        return new AuthenticatedApiContext(
            principal,
            principal.Identity?.IsAuthenticated == true,
            principal.FindFirstValue(PulumiAuthenticationDefaults.UserIdClaim)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier),
            principal.FindFirstValue(PulumiAuthenticationDefaults.LoginClaim)
                ?? principal.FindFirstValue(ClaimTypes.Name),
            principal.FindFirstValue(PulumiAuthenticationDefaults.OrgIdClaim),
            principal.FindFirstValue(PulumiAuthenticationDefaults.TeamIdClaim),
            principal.FindFirstValue(PulumiAuthenticationDefaults.AuthTokenClaim),
            principal.FindFirstValue(PulumiAuthenticationDefaults.UpdateTokenClaim),
            principal.FindFirstValue(PulumiAuthenticationDefaults.TokenTypeClaim),
            principal.FindFirstValue(PulumiAuthenticationDefaults.PrincipalTypeClaim),
            principal.FindFirstValue(PulumiAuthenticationDefaults.PrincipalIdClaim),
            bool.TryParse(principal.FindFirstValue(PulumiAuthenticationDefaults.ScopeAdminClaim), out var hasAdminScope) && hasAdminScope);
    }
}
