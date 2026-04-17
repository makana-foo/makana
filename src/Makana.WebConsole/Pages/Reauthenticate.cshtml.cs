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
public sealed class ReauthenticateModel(
    PulumiApiClient apiClient,
    WebConsoleLoginProviderCatalog loginProviderCatalog) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    public string? ResolvedOwner => ResolveEffectiveOwner();

    public OrganizationLoginDiscovery? OrganizationLogin { get; private set; }

    public IReadOnlyList<WebConsoleLoginProvider> Providers =>
        OrganizationLogin is null
            ? loginProviderCatalog.GetProviders(ResolvedOwner)
            : LoginModel.BuildOrganizationProvidersForTests(OrganizationLogin, loginProviderCatalog, ResolvedOwner);

    public async Task<IActionResult> OnGetAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        OrganizationLogin = await ResolveOrganizationLoginAsync(ResolvedOwner, cancellationToken);
        if (!await ShouldChallengeOrganizationLoginAsync(OrganizationLogin, cancellationToken) &&
            User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ResolveReturnUrl(returnUrl));
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAuthenticateAsync(string? provider = null, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var owner = ResolveEffectiveOwner(returnUrl);
        var organizationLogin = await ResolveOrganizationLoginAsync(owner, cancellationToken);
        if (!await ShouldChallengeOrganizationLoginAsync(organizationLogin, cancellationToken) &&
            User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(ResolveReturnUrl(returnUrl));
        }

        return BuildChallenge(provider, returnUrl, organizationLogin);
    }

    private ChallengeResult BuildChallenge(string? provider, string? returnUrl, OrganizationLoginDiscovery? organizationLogin)
    {
        var owner = ResolveEffectiveOwner(returnUrl);
        var properties = new AuthenticationProperties
        {
            RedirectUri = ResolveReturnUrl(returnUrl)
        };

        if (organizationLogin is not null &&
            !string.IsNullOrWhiteSpace(organizationLogin.ConnectionId) &&
            LoginModel.ShouldUseOrganizationDiscoveryForTests(provider, organizationLogin, owner))
        {
            properties.Items[WebConsoleLoginProviderCatalog.AuthenticationChallengeValueItemKey] = organizationLogin.ConnectionId;
            properties.Items[WebConsoleLoginProviderCatalog.AuthenticationProviderItemKey] = $"org:{organizationLogin.Organization}";
        }
        else
        {
            var selectedProvider = loginProviderCatalog.ResolveProvider(provider, owner);
            properties.Items[WebConsoleLoginProviderCatalog.AuthenticationProviderItemKey] = selectedProvider.Id;
        }

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

    private async Task<OrganizationLoginDiscovery?> ResolveOrganizationLoginAsync(string? owner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return null;
        }

        return await apiClient.GetOrganizationLoginAsync(owner, cancellationToken);
    }

    private async Task<bool> ShouldChallengeOrganizationLoginAsync(
        OrganizationLoginDiscovery? organizationLogin,
        CancellationToken cancellationToken)
    {
        if (organizationLogin is null)
        {
            return false;
        }

        var signedInOwners = await GetSignedInOwnersAsync(cancellationToken);
        return !signedInOwners.Contains(organizationLogin.Organization);
    }

    private async Task<IReadOnlySet<string>> GetSignedInOwnersAsync(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var authResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return WebConsoleAuthenticationState.GetSignedInOwners(authResult.Properties);
    }
}
