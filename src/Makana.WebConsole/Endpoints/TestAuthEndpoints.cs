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

        return app;
    }

    private static async Task<IResult> HandleSignInAsync(
        HttpContext context,
        string? user,
        string? returnUrl)
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
        properties.StoreTokens(
        [
            new AuthenticationToken
            {
                Name = "expires_at",
                Value = expiresUtc.ToString("o", CultureInfo.InvariantCulture)
            }
        ]);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
        return Results.Redirect(properties.RedirectUri!);
    }
}
