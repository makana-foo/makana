using Makana.WebConsole.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages;

[AllowAnonymous]
public sealed class ReauthenticateModel : PageModel
{
    public IReadOnlyList<WebConsoleLoginProvider> Providers { get; } = WebConsoleLoginProvider.All;

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

        var selectedProvider = WebConsoleLoginProvider.GetOrDefault(provider);
        var properties = new AuthenticationProperties
        {
            RedirectUri = ResolveReturnUrl(returnUrl)
        };
        properties.Items[WebConsoleLoginProvider.AuthenticationProviderItemKey] = selectedProvider.Id;
        return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
    }

    private string ResolveReturnUrl(string? returnUrl)
    {
        return Url.IsLocalUrl(returnUrl) ? returnUrl! : "/";
    }
}
