using System.Security.Claims;
using System.Text.Encodings.Web;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Makana.Api.Security;

internal sealed class PulumiTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    BackendStore store) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header))
        {
            return AuthenticateResult.NoResult();
        }

        var parts = header.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return AuthenticateResult.Fail("Invalid Authorization header format.");
        }

        var kind = parts[0];
        var token = parts[1].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.Fail("Missing token value.");
        }

        if (string.Equals(kind, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        if (string.Equals(kind, "token", StringComparison.OrdinalIgnoreCase))
        {
            var owner = await store.GetApiTokenOwnerAsync(token);
            if (owner is null)
            {
                return AuthenticateResult.Fail("Unknown API token.");
            }

            var claims = new List<Claim>
            {
                new(PulumiAuthenticationDefaults.AuthKindClaim, "token"),
                new(PulumiAuthenticationDefaults.AuthTokenClaim, token),
                new(ClaimTypes.NameIdentifier, owner.Value.UserId),
                new(PulumiAuthenticationDefaults.UserIdClaim, owner.Value.UserId),
            };
            if (!string.IsNullOrWhiteSpace(owner.Value.OrgId))
            {
                claims.Add(new Claim(PulumiAuthenticationDefaults.OrgIdClaim, owner.Value.OrgId!));
            }

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var apiTicket = new AuthenticationTicket(principal, Scheme.Name);
            return AuthenticateResult.Success(apiTicket);
        }

        if (!string.Equals(kind, "update-token", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Unsupported authorization scheme.");
        }

        var updateClaims = new[]
        {
            new Claim(PulumiAuthenticationDefaults.AuthKindClaim, "update-token"),
            new Claim(PulumiAuthenticationDefaults.AuthTokenClaim, token),
            new Claim(ClaimTypes.NameIdentifier, token),
        };

        var updateIdentity = new ClaimsIdentity(updateClaims, Scheme.Name);
        var updatePrincipal = new ClaimsPrincipal(updateIdentity);
        var updateTicket = new AuthenticationTicket(updatePrincipal, Scheme.Name);
        return AuthenticateResult.Success(updateTicket);
    }
}
