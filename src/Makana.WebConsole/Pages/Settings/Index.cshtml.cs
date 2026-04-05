using System.Net;
using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Settings;

public sealed class IndexModel(
    ConsoleContextService consoleContextService,
    PulumiApiClient apiClient,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    [BindProperty]
    public string InviteEmail { get; set; } = string.Empty;

    [BindProperty]
    public string InviteRole { get; set; } = "member";

    [BindProperty]
    public string TokenName { get; set; } = string.Empty;

    [BindProperty]
    public string? TokenDescription { get; set; }

    [BindProperty]
    public int? TokenExpiresInDays { get; set; }

    [BindProperty]
    public string SsoProtocol { get; set; } = "oidc";

    [BindProperty]
    public string SsoMode { get; set; } = "optional";

    [BindProperty]
    public string? SsoDisplayName { get; set; }

    [BindProperty]
    public string SsoDomains { get; set; } = string.Empty;

    [BindProperty]
    public string? SsoScimBearerToken { get; set; }

    [BindProperty]
    public string? SsoOidcIssuer { get; set; }

    [BindProperty]
    public string? SsoOidcClientId { get; set; }

    [BindProperty]
    public string? SsoOidcClientSecret { get; set; }

    [BindProperty]
    public string? SsoSamlSsoUrl { get; set; }

    [BindProperty]
    public string? SsoSamlCaCertificatePem { get; set; }

    [BindProperty]
    public string SsoSamlUsernameAttribute { get; set; } = "name";

    [BindProperty]
    public string SsoSamlEmailAttribute { get; set; } = "email";

    [BindProperty]
    public string? SsoSamlGroupsAttribute { get; set; }

    [BindProperty]
    public string? SsoSamlEntityIssuer { get; set; }

    [BindProperty]
    public string? SsoSamlIssuer { get; set; }

    [BindProperty]
    public string? SsoSamlGroupsDelimiter { get; set; }

    [BindProperty]
    public string? SsoSamlNameIdPolicyFormat { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    public ConsoleNavigationContext Context { get; private set; } = ConsoleNavigationContext.Empty;

    public bool IsPersonalSettings { get; private set; }

    public bool CanAdministerOrganization { get; private set; }

    public IReadOnlyList<ServiceThemeOption> AvailableThemes => Context.User?.AvailableThemes ?? [];

    public string? SelectedTheme => Context.User?.Theme;

    public IReadOnlyList<OrganizationMember> Members { get; private set; } = [];

    public IReadOnlyList<OrganizationInvite> Invites { get; private set; } = [];

    public IReadOnlyList<OrganizationTokenSummary> Tokens { get; private set; } = [];

    public OrganizationSsoConfiguration? SsoConfiguration { get; private set; }

    public CreateOrganizationTokenResponse? CreatedToken { get; private set; }

    public bool HasOrganizationSso => SsoConfiguration is not null;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostInviteAsync(CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!CanAdministerOrganization)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(InviteEmail))
        {
            ModelState.AddModelError(nameof(InviteEmail), "Invite email is required.");
            return Page();
        }

        try
        {
            await apiClient.CreateOrganizationInviteAsync(Owner!, InviteEmail, NormalizeRole(InviteRole), cancellationToken);
            SetStatus("success", "Organization invite created.");
            return RedirectToPage(new { owner = Owner });
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict)
        {
            logger.LogWarning(ex, "Failed creating organization invite for {Owner}", Owner);
            ModelState.AddModelError(nameof(InviteEmail), "Unable to create that invite.");
            await ReloadOrganizationAdministrationAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateMemberRoleAsync(string userId, string role, CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!CanAdministerOrganization)
        {
            return Forbid();
        }

        await apiClient.UpdateOrganizationMemberRoleAsync(Owner!, userId, NormalizeRole(role), cancellationToken);
        SetStatus("success", "Organization member role updated.");
        return RedirectToPage(new { owner = Owner });
    }

    public async Task<IActionResult> OnPostRevokeInviteAsync(string inviteId, CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!CanAdministerOrganization)
        {
            return Forbid();
        }

        await apiClient.RevokeOrganizationInviteAsync(Owner!, inviteId, cancellationToken);
        SetStatus("success", "Organization invite revoked.");
        return RedirectToPage(new { owner = Owner });
    }

    public async Task<IActionResult> OnPostRemoveMemberAsync(string userId, CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!CanAdministerOrganization)
        {
            return Forbid();
        }

        await apiClient.RemoveOrganizationMemberAsync(Owner!, userId, cancellationToken);
        SetStatus("success", "Organization member removed.");
        return RedirectToPage(new { owner = Owner });
    }

    public async Task<IActionResult> OnPostCreateTokenAsync(CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!CanAdministerOrganization)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(TokenName))
        {
            ModelState.AddModelError(nameof(TokenName), "Token name is required.");
            return Page();
        }

        CreatedToken = await apiClient.CreateOrganizationTokenAsync(
            Owner!,
            TokenName,
            TokenDescription,
            TokenExpiresInDays,
            cancellationToken);
        SetStatus("success", "Organization token created. Copy it now; Makana will not show it again.");
        await ReloadOrganizationAdministrationAsync(cancellationToken);
        TokenName = string.Empty;
        TokenDescription = string.Empty;
        TokenExpiresInDays = null;
        return Page();
    }

    public async Task<IActionResult> OnPostRevokeTokenAsync(string tokenId, CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!CanAdministerOrganization)
        {
            return Forbid();
        }

        await apiClient.RevokeOrganizationTokenAsync(Owner!, tokenId, cancellationToken);
        SetStatus("success", "Organization token revoked.");
        return RedirectToPage(new { owner = Owner });
    }

    public async Task<IActionResult> OnPostSaveSsoAsync(CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken, populateSsoInputs: false);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!CanAdministerOrganization)
        {
            return Forbid();
        }

        var request = new UpdateOrganizationSsoRequest(
            NormalizeSsoProtocol(SsoProtocol),
            NormalizeSsoMode(SsoMode),
            NullIfWhiteSpace(SsoDisplayName),
            ParseDomains(SsoDomains),
            NullIfWhiteSpace(SsoScimBearerToken),
            BuildOidcConfiguration(),
            BuildSamlConfiguration());
        SsoConfiguration = await apiClient.UpsertOrganizationSsoAsync(Owner!, request, cancellationToken);
        SetStatus("success", "Organization SSO settings saved.");
        await ReloadOrganizationAdministrationAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteSsoAsync(CancellationToken cancellationToken)
    {
        var loadResult = await LoadPageStateAsync(cancellationToken);
        if (loadResult is not null)
        {
            return loadResult;
        }

        if (!CanAdministerOrganization)
        {
            return Forbid();
        }

        await apiClient.DeleteOrganizationSsoAsync(Owner!, cancellationToken);
        SetStatus("success", "Organization SSO settings removed.");
        return RedirectToPage(new { owner = Owner });
    }

    private async Task<IActionResult?> LoadPageStateAsync(CancellationToken cancellationToken, bool populateSsoInputs = true)
    {
        Context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (Context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        Owner = Context.SelectedOwner;
        IsPersonalSettings = Context.SelectedOwnerIsPersonal;

        if (!IsPersonalSettings)
        {
            try
            {
                Members = await apiClient.GetOrganizationMembersAsync(Owner!, cancellationToken);
                await ReloadOrganizationAdministrationAsync(cancellationToken, populateSsoInputs);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
            {
                logger.LogWarning(ex, "Forbidden loading organization settings for {Owner}", Owner);
                Members = [];
                Invites = [];
                Tokens = [];
                SsoConfiguration = null;
                CanAdministerOrganization = false;
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogWarning(ex, "Organization settings owner route not found for {Owner}", Owner);
                return ConsolePageResults.ConsoleNotFound(this);
            }
        }

        return null;
    }

    private async Task ReloadOrganizationAdministrationAsync(CancellationToken cancellationToken, bool populateSsoInputs = true)
    {
        if (IsPersonalSettings || string.IsNullOrWhiteSpace(Owner))
        {
            return;
        }

        try
        {
            Invites = await apiClient.GetOrganizationInvitesAsync(Owner, cancellationToken);
            Tokens = await apiClient.GetOrganizationTokensAsync(Owner, cancellationToken);
            SsoConfiguration = await apiClient.GetOrganizationSsoAsync(Owner, cancellationToken);
            CanAdministerOrganization = true;

            if (populateSsoInputs && SsoConfiguration is not null)
            {
                SsoProtocol = SsoConfiguration.Protocol;
                SsoMode = SsoConfiguration.Mode;
                SsoDisplayName = SsoConfiguration.DisplayName;
                SsoDomains = string.Join(", ", SsoConfiguration.Domains);
                SsoScimBearerToken = SsoConfiguration.ScimBearerToken;
                SsoOidcIssuer = SsoConfiguration.Oidc?.Issuer;
                SsoOidcClientId = SsoConfiguration.Oidc?.ClientId;
                SsoOidcClientSecret = SsoConfiguration.Oidc?.ClientSecret;
                SsoSamlSsoUrl = SsoConfiguration.Saml?.SsoUrl;
                SsoSamlCaCertificatePem = SsoConfiguration.Saml?.CaCertificatePem;
                SsoSamlUsernameAttribute = SsoConfiguration.Saml?.UsernameAttribute ?? "name";
                SsoSamlEmailAttribute = SsoConfiguration.Saml?.EmailAttribute ?? "email";
                SsoSamlGroupsAttribute = SsoConfiguration.Saml?.GroupsAttribute;
                SsoSamlEntityIssuer = SsoConfiguration.Saml?.EntityIssuer;
                SsoSamlIssuer = SsoConfiguration.Saml?.SsoIssuer;
                SsoSamlGroupsDelimiter = SsoConfiguration.Saml?.GroupsDelimiter;
                SsoSamlNameIdPolicyFormat = SsoConfiguration.Saml?.NameIdPolicyFormat;
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            logger.LogDebug(ex, "Non-admin organization settings view for {Owner}", Owner);
            Invites = [];
            Tokens = [];
            SsoConfiguration = null;
            CanAdministerOrganization = false;
        }
    }

    private void SetStatus(string kind, string message)
    {
        StatusKind = kind;
        StatusMessage = message;
    }

    private static string NormalizeRole(string role)
        => string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase) ? "admin" : "member";

    private static string NormalizeSsoMode(string mode)
        => mode.ToLowerInvariant() switch
        {
            "required" => "required",
            "exclusive" => "exclusive",
            _ => "optional"
        };

    private static string NormalizeSsoProtocol(string protocol)
        => string.Equals(protocol, "saml", StringComparison.OrdinalIgnoreCase) ? "saml" : "oidc";

    private static string[] ParseDomains(string? domains)
        => (domains ?? string.Empty)
            .Split([',', '\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private OrganizationOidcConfigurationRequest? BuildOidcConfiguration()
        => string.Equals(SsoProtocol, "oidc", StringComparison.OrdinalIgnoreCase)
            ? new OrganizationOidcConfigurationRequest(
                NullIfWhiteSpace(SsoOidcIssuer) ?? string.Empty,
                NullIfWhiteSpace(SsoOidcClientId) ?? string.Empty,
                NullIfWhiteSpace(SsoOidcClientSecret) ?? string.Empty)
            : null;

    private OrganizationSamlConfigurationRequest? BuildSamlConfiguration()
        => string.Equals(SsoProtocol, "saml", StringComparison.OrdinalIgnoreCase)
            ? new OrganizationSamlConfigurationRequest(
                NullIfWhiteSpace(SsoSamlSsoUrl) ?? string.Empty,
                NullIfWhiteSpace(SsoSamlCaCertificatePem) ?? string.Empty,
                NullIfWhiteSpace(SsoSamlUsernameAttribute) ?? string.Empty,
                NullIfWhiteSpace(SsoSamlEmailAttribute) ?? string.Empty,
                NullIfWhiteSpace(SsoSamlGroupsAttribute),
                NullIfWhiteSpace(SsoSamlEntityIssuer),
                NullIfWhiteSpace(SsoSamlIssuer),
                NullIfWhiteSpace(SsoSamlGroupsDelimiter),
                NullIfWhiteSpace(SsoSamlNameIdPolicyFormat))
            : null;
}
