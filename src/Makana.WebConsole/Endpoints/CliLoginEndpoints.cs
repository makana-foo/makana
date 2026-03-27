using System.Text.Encodings.Web;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Makana.WebConsole.Endpoints;

internal static class CliLoginEndpoints
{
    public static IEndpointRouteBuilder MapCliLoginEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/cli-login", HandleCliLoginAsync).AllowAnonymous();

        var stackRunRedirects = app.MapGroup("/{owner}/{project}/{stack}")
            .RequireAuthorization();
        stackRunRedirects.MapGet("/{runType}/{updateRef}", HandleStackRunRedirect);

        return app;
    }

    private static async Task<Results<BadRequest, ChallengeHttpResult, RedirectHttpResult>> HandleCliLoginAsync(
        HttpContext context,
        [AsParameters] CliLoginQuery query,
        PulumiCliLoginService cliLoginService)
    {
        if (query is not { CliSessionPort: > 0, CliSessionNonce: { Length: > 0 } })
        {
            return TypedResults.BadRequest();
        }

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

        var callbackUri = $"http://127.0.0.1:{query.CliSessionPort.Value}/?accessToken={UrlEncoder.Default.Encode(accessToken)}&nonce={UrlEncoder.Default.Encode(query.CliSessionNonce)}";

        return TypedResults.Redirect(callbackUri);
    }

    private static ChallengeHttpResult BuildCliLoginChallenge(HttpContext context)
    {
        var returnUrl = context.Request.Path + context.Request.QueryString;
        var properties = new AuthenticationProperties { RedirectUri = returnUrl };
        return TypedResults.Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }

    private static Results<NotFound, RedirectHttpResult> HandleStackRunRedirect(
        string owner,
        string project,
        string stack,
        string runType,
        string updateRef)
    {
        if (!IsSupportedRunType(runType))
        {
            return TypedResults.NotFound();
        }

        var target = $"/{Uri.EscapeDataString(owner)}/stacks/{Uri.EscapeDataString(project)}/{Uri.EscapeDataString(stack)}/{Uri.EscapeDataString(runType)}/{Uri.EscapeDataString(updateRef)}";
        return TypedResults.Redirect(target, permanent: false);
    }

    private static bool IsSupportedRunType(string runType)
    {
        return runType is "previews" or "updates" or "refreshes" or "destroys";
    }
}

internal sealed record CliLoginQuery(int? CliSessionPort, string? CliSessionNonce, string? CliSessionDescription);
