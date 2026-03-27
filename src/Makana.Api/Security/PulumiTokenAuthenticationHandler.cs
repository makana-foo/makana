using System.Security.Claims;
using System.Text.Encodings.Web;
using Makana.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Makana.Api.Security;

internal class PulumiTokenOptions : AuthenticationSchemeOptions
{

}

internal sealed class PulumiApiTokenAuthenticationHandler(
    IOptionsMonitor<PulumiTokenOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IdentityRepository identityRepository) : AuthenticationHandler<PulumiTokenOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!PulumiAuthorizationTokenReader.TryReadAuthorizationToken(Request, out var kind, out var token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!string.Equals(kind, "token", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var apiToken = identityRepository.GetApiToken(token);
        if (apiToken is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Unknown API token."));
        }

        var claims = new List<Claim>
        {
            new(PulumiAuthenticationDefaults.AuthTokenClaim, token),
            new(PulumiAuthenticationDefaults.UserIdClaim, apiToken.UserId),
            new(ClaimTypes.NameIdentifier, apiToken.UserId),
            new(PulumiAuthenticationDefaults.TokenTypeClaim, apiToken.TokenType),
            new(PulumiAuthenticationDefaults.PrincipalTypeClaim, apiToken.PrincipalType),
            new(PulumiAuthenticationDefaults.PrincipalIdClaim, apiToken.PrincipalId),
            new(PulumiAuthenticationDefaults.ScopeAdminClaim, apiToken.ScopeAdmin.ToString()),
        };

        var user = identityRepository.GetUserById(apiToken.UserId);
        if (user is { Login: { Length: > 0 } login })
        {
            claims.Add(new Claim(PulumiAuthenticationDefaults.LoginClaim, login));
            claims.Add(new Claim(ClaimTypes.Name, login));
        }

        if (!string.IsNullOrWhiteSpace(apiToken.OrgId))
        {
            claims.Add(new Claim(PulumiAuthenticationDefaults.OrgIdClaim, apiToken.OrgId!));
        }

        if (!string.IsNullOrWhiteSpace(apiToken.TeamId))
        {
            claims.Add(new Claim(PulumiAuthenticationDefaults.TeamIdClaim, apiToken.TeamId!));
        }

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal sealed class PulumiUpdateTokenAuthenticationHandler(
    IOptionsMonitor<PulumiTokenOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<PulumiTokenOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!PulumiAuthorizationTokenReader.TryReadAuthorizationToken(Request, out var kind, out var token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!string.Equals(kind, "update-token", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[]
        {
            new Claim(PulumiAuthenticationDefaults.UpdateTokenClaim, token),
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

internal static class PulumiAuthorizationTokenReader
{
    private static readonly PathString StackUpdateHubPath = new("/hubs/stack-updates");

    public static bool TryReadAuthorizationToken(HttpRequest request, out string kind, out string token)
    {
        if (TryParseAuthorizationValue(request.Headers.Authorization.ToString(), out kind, out token))
        {
            return true;
        }

        if (request.Path.StartsWithSegments(StackUpdateHubPath) &&
            TryParseAuthorizationValue(request.Query["access_token"].ToString(), out kind, out token))
        {
            return true;
        }

        kind = string.Empty;
        token = string.Empty;
        return false;
    }

    public static bool TryParseAuthorizationValue(string rawValue, out string kind, out string token)
    {
        kind = string.Empty;
        token = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var parts = rawValue.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        kind = parts[0];
        token = parts[1];
        return true;
    }
}
