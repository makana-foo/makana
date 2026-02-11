using System.Text.Encodings.Web;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http.Metadata;

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
        if (context.User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = context.Request.Path + context.Request.QueryString;
            var location = "/Login?returnUrl=" + UrlEncoder.Default.Encode(returnUrl);
            return Results.Redirect(location);
        }

        var idToken = await context.GetTokenAsync("id_token");
        if (string.IsNullOrWhiteSpace(idToken))
        {
            var returnUrl = context.Request.Path + context.Request.QueryString;
            var properties = new AuthenticationProperties { RedirectUri = returnUrl };
            return Results.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
        }

        var accessToken = await cliLoginService.ExchangeIdTokenAsync(idToken, context.RequestAborted);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return Results.Problem(
                title: "CLI login failed",
                detail: "Unable to exchange identity token for an API access token.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var callbackUri = $"http://127.0.0.1:{query.CliSessionPort}/?accessToken={UrlEncoder.Default.Encode(accessToken)}&nonce={UrlEncoder.Default.Encode(query.CliSessionNonce)}";
        return Results.Redirect(callbackUri);
    }
}

internal sealed class CliLoginQuery
{
    public int CliSessionPort { get; init; }

    public string CliSessionNonce { get; init; } = string.Empty;

    public string? CliSessionDescription { get; init; }
}
