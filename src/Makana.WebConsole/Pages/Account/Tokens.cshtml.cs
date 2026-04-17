using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Account;

public sealed class TokensModel(
    PulumiCliLoginService cliLoginService,
    IWebConsoleApiTokenAccessor apiTokenAccessor) : PageModel
{
    public string AccessToken { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var idToken = await HttpContext.GetTokenAsync("id_token");
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return BuildChallenge();
        }

        var currentApiToken = await apiTokenAccessor.GetApiTokenAsync(cancellationToken);
        var accessToken = await cliLoginService.ExchangeIdTokenAsync(idToken, currentApiToken, cancellationToken);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return BuildChallenge();
        }

        AccessToken = accessToken;
        return Page();
    }

    private ChallengeResult BuildChallenge()
    {
        var returnUrl = Url.Page("/Account/Tokens") ?? "/account/tokens";
        var properties = new AuthenticationProperties { RedirectUri = returnUrl };
        return Challenge(properties, [OpenIdConnectDefaults.AuthenticationScheme]);
    }
}
