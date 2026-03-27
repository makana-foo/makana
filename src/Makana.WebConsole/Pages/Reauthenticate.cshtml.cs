using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages;

[AllowAnonymous]
public sealed class ReauthenticateModel(WebConsoleLoginProviderCatalog loginProviderCatalog) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    public string? ResolvedOwner => ResolveEffectiveOwner();

    public IReadOnlyList<WebConsoleLoginProvider> Providers => loginProviderCatalog.GetProviders(ResolvedOwner);

    public IActionResult OnGet(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ResolveReturnUrl(returnUrl));
        }

        return Page();
    }

    public IActionResult OnPostAuthenticate(string? provider = null, string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ResolveReturnUrl(returnUrl));
        }

        var owner = ResolveEffectiveOwner(returnUrl);
        var selectedProvider = loginProviderCatalog.ResolveProvider(provider, owner);
        var properties = new AuthenticationProperties
        {
            RedirectUri = ResolveReturnUrl(returnUrl)
        };
        properties.Items[WebConsoleLoginProviderCatalog.AuthenticationProviderItemKey] = selectedProvider.Id;
        if (!string.IsNullOrWhiteSpace(owner))
        {
            properties.Items[WebConsoleLoginProviderCatalog.AuthenticationOwnerItemKey] = owner;
        }

        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    private string ResolveReturnUrl(string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return returnUrl!;
        }

        var owner = ResolveEffectiveOwner(returnUrl);
        return string.IsNullOrWhiteSpace(owner)
            ? "/"
            : ConsoleRoutePaths.OwnerDashboard(owner);
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
