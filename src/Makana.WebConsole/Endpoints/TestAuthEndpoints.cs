using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Makana.WebConsole.Endpoints;

internal static class TestAuthEndpoints
{
    public static IEndpointRouteBuilder MapTestAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/__test/sign-in", HandleSignInAsync)
            .AllowAnonymous();
        app.MapGet("/__test/session-tokens", (Delegate)HandleSessionTokensAsync)
            .AllowAnonymous();

        return app;
    }

    private static async Task<IResult> HandleSignInAsync(
        HttpContext context,
        string? user,
        string? returnUrl,
        string? accessToken,
        string? expiresAt)
    {
        var userName = string.IsNullOrWhiteSpace(user) ? "testuser" : user;
        var issuedUtc = DateTimeOffset.UtcNow;
        var expiresUtc = issuedUtc.AddHours(1);

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, $"development:{userName}"),
                new Claim(ClaimTypes.Name, userName)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme));

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = issuedUtc,
            ExpiresUtc = expiresUtc,
            RedirectUri = string.IsNullOrWhiteSpace(returnUrl) ? "/" : returnUrl
        };

        var tokens = new List<AuthenticationToken>
        {
            new()
            {
                Name = "expires_at",
                Value = expiresUtc.ToString("o", CultureInfo.InvariantCulture)
            }
        };

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            tokens.Add(new AuthenticationToken
            {
                Name = "access_token",
                Value = accessToken
            });
        }

        if (!string.IsNullOrWhiteSpace(expiresAt))
        {
            tokens.RemoveAll(static token => string.Equals(token.Name, "expires_at", StringComparison.Ordinal));
            tokens.Add(new AuthenticationToken
            {
                Name = "expires_at",
                Value = expiresAt
            });
        }

        properties.StoreTokens(tokens);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
        return Results.Redirect(properties.RedirectUri!);
    }

    private static async Task<IResult> HandleSessionTokensAsync(HttpContext context)
    {
        var authResult = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!authResult.Succeeded)
        {
            return Results.Unauthorized();
        }

        var accessToken = await context.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, "access_token")
            ?? authResult.Properties?.GetTokenValue("access_token");
        var idToken = await context.GetTokenAsync(CookieAuthenticationDefaults.AuthenticationScheme, "id_token")
            ?? authResult.Properties?.GetTokenValue("id_token");
        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(idToken))
        {
            return Results.BadRequest();
        }

        return Results.Json(new SessionTokensResponse(accessToken, idToken));
    }

    private sealed record SessionTokensResponse(string AccessToken, string IdToken);
}
