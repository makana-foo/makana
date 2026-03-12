using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages;

[AllowAnonymous]
public sealed class LoginModel(ConsoleContextService consoleContextService) : PageModel
{
    public IReadOnlyList<WebConsoleLoginProvider> Providers { get; } = WebConsoleLoginProvider.All;

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(await ResolveAuthenticatedReturnUrlAsync(returnUrl, cancellationToken));
        }

        return Page();
    }

    public async Task<IActionResult> OnGetSignInAsync(string? provider = null, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(await ResolveAuthenticatedReturnUrlAsync(returnUrl, cancellationToken));
        }

        return BuildChallenge(provider, returnUrl);
    }

    public async Task<IActionResult> OnPostSignInAsync(string? provider = null, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(await ResolveAuthenticatedReturnUrlAsync(returnUrl, cancellationToken));
        }

        return BuildChallenge(provider, returnUrl);
    }

    public IActionResult OnPostSignOut()
    {
        var properties = new AuthenticationProperties { RedirectUri = "/Login" };
        return SignOut(
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    private ChallengeResult BuildChallenge(string? provider, string? returnUrl)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = ResolvePostLoginReturnUrl(returnUrl)
        };

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var selectedProvider = WebConsoleLoginProvider.GetOrDefault(provider);
            properties.Items[WebConsoleLoginProvider.AuthenticationProviderItemKey] = selectedProvider.Id;
        }

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    private string ResolvePostLoginReturnUrl(string? returnUrl)
    {
        return Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Login";
    }

    private async Task<string> ResolveAuthenticatedReturnUrlAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return returnUrl!;
        }

        var context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        return ConsoleRoutePaths.OwnerDashboard(context.SelectedOwner);
    }
}
