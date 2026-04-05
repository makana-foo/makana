using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Makana.WebConsole.Pages;

[AllowAnonymous]
public sealed class LoginModel(
    ConsoleContextService consoleContextService,
    PulumiApiClient apiClient,
    WebConsoleLoginProviderCatalog loginProviderCatalog) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    public string? ResolvedOwner => ResolveEffectiveOwner();

    public OrganizationLoginDiscovery? OrganizationLogin { get; private set; }

    public IReadOnlyList<WebConsoleLoginProvider> Providers =>
        OrganizationLogin is null
            ? loginProviderCatalog.GetProviders(ResolvedOwner)
            : BuildOrganizationProvidersForTests(OrganizationLogin, loginProviderCatalog, ResolvedOwner);

    public async Task<IActionResult> OnGetAsync(
        string? returnUrl = null,
        bool showProviders = false,
        CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(await ResolveAuthenticatedReturnUrlAsync(returnUrl, cancellationToken));
        }

        OrganizationLogin = await ResolveOrganizationLoginAsync(ResolvedOwner, cancellationToken);

        if (OrganizationLogin is not null && !showProviders)
        {
            return BuildChallenge(null, returnUrl, OrganizationLogin);
        }

        if (!showProviders && string.IsNullOrWhiteSpace(ResolvedOwner) && Providers.Count == 1)
        {
            return BuildChallenge(Providers[0].Id, returnUrl, null);
        }

        return Page();
    }

    public async Task<IActionResult> OnGetSignInAsync(string? provider = null, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(await ResolveAuthenticatedReturnUrlAsync(returnUrl, cancellationToken));
        }

        OrganizationLogin = await ResolveOrganizationLoginAsync(ResolveEffectiveOwner(returnUrl), cancellationToken);
        return BuildChallenge(provider, returnUrl, OrganizationLogin);
    }

    public async Task<IActionResult> OnPostSignInAsync(string? provider = null, string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(await ResolveAuthenticatedReturnUrlAsync(returnUrl, cancellationToken));
        }

        OrganizationLogin = await ResolveOrganizationLoginAsync(ResolveEffectiveOwner(returnUrl), cancellationToken);
        return BuildChallenge(provider, returnUrl, OrganizationLogin);
    }

    public async Task<IActionResult> OnPostDiscoverAsync(string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect(await ResolveAuthenticatedReturnUrlAsync(returnUrl, cancellationToken));
        }

        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@', StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(Email), "Enter a work email address to look up organization sign-in.");
            return Page();
        }

        var discovery = await apiClient.DiscoverOrganizationLoginAsync(Email, cancellationToken);
        if (discovery is null || string.IsNullOrWhiteSpace(discovery.ConnectionId))
        {
            ModelState.AddModelError(nameof(Email), "No organization SSO route matched that email address.");
            return Page();
        }

        Owner = discovery.Organization;
        OrganizationLogin = discovery;
        return BuildChallenge(null, returnUrl, discovery);
    }

    public async Task<IActionResult> OnPostSignOutAsync(CancellationToken cancellationToken = default)
    {
        var properties = new AuthenticationProperties
        {
            RedirectUri = ConsoleRoutePaths.Splash()
        };

        return await SupportsRemoteSignOutAsync(HttpContext, cancellationToken)
            ? SignOut(
                properties,
                CookieAuthenticationDefaults.AuthenticationScheme,
                OpenIdConnectDefaults.AuthenticationScheme)
            : SignOut(
                properties,
                CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static async Task<bool> SupportsRemoteSignOutAsync(HttpContext httpContext, CancellationToken cancellationToken)
    {
        var optionsMonitor = httpContext.RequestServices.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>();
        var options = optionsMonitor.Get(OpenIdConnectDefaults.AuthenticationScheme);

        if (!string.IsNullOrWhiteSpace(options.Configuration?.EndSessionEndpoint))
        {
            return true;
        }

        if (options.ConfigurationManager is null)
        {
            return false;
        }

        var configuration = await options.ConfigurationManager.GetConfigurationAsync(cancellationToken);
        return !string.IsNullOrWhiteSpace(configuration.EndSessionEndpoint);
    }

    private ChallengeResult BuildChallenge(string? provider, string? returnUrl, OrganizationLoginDiscovery? organizationLogin)
    {
        var owner = ResolveEffectiveOwner(returnUrl);
        var properties = new AuthenticationProperties
        {
            RedirectUri = ResolvePostLoginReturnUrl(returnUrl, owner)
        };

        if (organizationLogin is not null &&
            !string.IsNullOrWhiteSpace(organizationLogin.ConnectionId) &&
            ShouldUseOrganizationDiscoveryForTests(provider, organizationLogin, owner))
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

    private async Task<OrganizationLoginDiscovery?> ResolveOrganizationLoginAsync(string? owner, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return null;
        }

        return await apiClient.GetOrganizationLoginAsync(owner, cancellationToken);
    }

    internal static IReadOnlyList<WebConsoleLoginProvider> BuildOrganizationProvidersForTests(
        OrganizationLoginDiscovery organizationLogin,
        WebConsoleLoginProviderCatalog loginProviderCatalog,
        string? owner)
    {
        var ssoProvider = new WebConsoleLoginProvider(
            $"org:{organizationLogin.Organization}",
            "SS",
            organizationLogin.DisplayName ?? $"{organizationLogin.Organization} {FormatProtocolLabel(organizationLogin.Protocol)}",
            $"Continue with {organizationLogin.DisplayName ?? organizationLogin.Organization}",
            organizationLogin.ConnectionId ?? organizationLogin.Organization);

        if (!string.Equals(organizationLogin.Mode, "optional", StringComparison.OrdinalIgnoreCase))
        {
            return [ssoProvider];
        }

        List<WebConsoleLoginProvider> providers = [ssoProvider];
        providers.AddRange(loginProviderCatalog.GetProviders(owner));
        return providers;
    }

    internal static bool ShouldUseOrganizationDiscoveryForTests(string? provider, OrganizationLoginDiscovery organizationLogin, string? owner)
    {
        if (string.IsNullOrWhiteSpace(organizationLogin.ConnectionId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(provider))
        {
            return !string.IsNullOrWhiteSpace(owner);
        }

        return string.Equals(provider, $"org:{organizationLogin.Organization}", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(provider, organizationLogin.ConnectionId, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatProtocolLabel(string? protocol)
        => string.Equals(protocol, "saml", StringComparison.OrdinalIgnoreCase)
            ? "SAML SSO"
            : "OIDC SSO";
}
