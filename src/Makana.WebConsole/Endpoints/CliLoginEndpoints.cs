using System.Text.Encodings.Web;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Makana.WebConsole.Endpoints;

internal static class CliLoginEndpoints
{
    public static IEndpointRouteBuilder MapCliLoginEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cli-login", HandleCliLoginAsync).AllowAnonymous();

        return app;
    }

    private static async Task<IResult> HandleCliLoginAsync(
        HttpContext context,
        [AsParameters] CliLoginQuery query,
        PulumiCliLoginService cliLoginService)
    {
        if (context is not { User.Identity.IsAuthenticated: true })
        {
            return BuildCliLoginChallenge(context);
        }

        var idToken = await context.GetTokenAsync("id_token");
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return BuildCliLoginChallenge(context);
        }

        var accessToken = await cliLoginService.ExchangeIdTokenAsync(idToken, context.RequestAborted);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return BuildCliLoginChallenge(context);
        }

        var callbackUri = $"http://127.0.0.1:{query.CliSessionPort}/?accessToken={UrlEncoder.Default.Encode(accessToken)}&nonce={UrlEncoder.Default.Encode(query.CliSessionNonce)}";

        return Results.Redirect(callbackUri);
    }

    private static IResult BuildCliLoginChallenge(HttpContext context)
    {
        var returnUrl = context.Request.Path + context.Request.QueryString;
        var properties = new AuthenticationProperties { RedirectUri = returnUrl };
        return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }
}

internal sealed record CliLoginQuery(int CliSessionPort, string CliSessionNonce, string? CliSessionDescription);
