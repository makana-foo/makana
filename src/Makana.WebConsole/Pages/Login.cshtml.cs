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
public sealed class LoginModel(
    ConsoleContextService consoleContextService,
    WebConsoleLoginProviderCatalog loginProviderCatalog) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    public string? ResolvedOwner => ResolveEffectiveOwner();

    public IReadOnlyList<WebConsoleLoginProvider> Providers => loginProviderCatalog.GetProviders(ResolvedOwner);

    public async Task<IActionResult> OnGetAsync(
        string? returnUrl = null,
        bool showProviders = false,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(await ResolveAuthenticatedReturnUrlAsync(returnUrl, cancellationToken));
        }

        if (!showProviders && Providers.Count == 1)
        {
            return BuildChallenge(Providers[0].Id, returnUrl);
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
        var properties = new AuthenticationProperties
        {
            RedirectUri = ConsoleRoutePaths.Login()
        };
        return SignOut(
            properties,
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    private ChallengeResult BuildChallenge(string? provider, string? returnUrl)
    {
        var owner = ResolveEffectiveOwner(returnUrl);
        var selectedProvider = loginProviderCatalog.ResolveProvider(provider, owner);
        var properties = new AuthenticationProperties
        {
            RedirectUri = ResolvePostLoginReturnUrl(returnUrl, owner)
        };
        properties.Items[WebConsoleLoginProviderCatalog.AuthenticationProviderItemKey] = selectedProvider.Id;
        if (!string.IsNullOrWhiteSpace(owner))
        {
            properties.Items[WebConsoleLoginProviderCatalog.AuthenticationOwnerItemKey] = owner;
        }

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    private string ResolvePostLoginReturnUrl(string? returnUrl, string? owner)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return returnUrl!;
        }

        return string.IsNullOrWhiteSpace(owner)
            ? ConsoleRoutePaths.Login()
            : ConsoleRoutePaths.OwnerDashboard(owner);
    }

    private async Task<string> ResolveAuthenticatedReturnUrlAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return returnUrl!;
        }

        var owner = ResolveEffectiveOwner(returnUrl);
        if (!string.IsNullOrWhiteSpace(owner))
        {
            return ConsoleRoutePaths.OwnerDashboard(owner);
        }

        var context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        return ConsoleRoutePaths.OwnerDashboard(context.SelectedOwner);
    }

    private string? ResolveEffectiveOwner(string? returnUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(Owner))
        {
            return Owner;
        }

        var candidateReturnUrl = returnUrl ?? Request.Query["returnUrl"].ToString();
        return ConsoleRoutePaths.ResolveOwnerFromLocalReturnUrl(candidateReturnUrl);
    }
}
