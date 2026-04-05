using Makana.WebConsole.Configuration;
using Makana.WebConsole.Models;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace Makana.WebConsole.Services;

public sealed class WebConsoleLoginProviderCatalog
{
    public const string AuthenticationProviderItemKey = "makana.auth.provider";
    public const string AuthenticationOwnerItemKey = "makana.auth.owner";
    public const string AuthenticationChallengeValueItemKey = "makana.auth.challenge";

    private readonly IReadOnlyList<WebConsoleLoginProvider> _globalProviders;
    private readonly Dictionary<string, OrganizationProviderSelection> _organizationProviders;

    public WebConsoleLoginProviderCatalog(
        IOptions<WebConsoleAuthenticationOptions> authenticationOptions,
        IConfiguration configuration)
    {
        ChallengeParameterName = string.Equals(configuration["OIDC_PROVIDER"], "dex", StringComparison.OrdinalIgnoreCase)
            ? "connector_id"
            : "provider";

        _globalProviders = BuildGlobalProviders(authenticationOptions.Value, ChallengeParameterName);
        if (_globalProviders.Count == 0)
        {
            throw new InvalidOperationException("At least one Web Console authentication provider must be available.");
        }

        DefaultProvider = _globalProviders[0];
        _organizationProviders = BuildOrganizationProviders(authenticationOptions.Value, _globalProviders);
    }

    public string ChallengeParameterName { get; }

    public WebConsoleLoginProvider DefaultProvider { get; }

    public IReadOnlyList<WebConsoleLoginProvider> GetProviders(string? owner)
    {
        if (string.IsNullOrWhiteSpace(owner) ||
            !_organizationProviders.TryGetValue(owner, out var selection))
        {
            return _globalProviders;
        }

        if (!selection.AllowGlobalProviders)
        {
            return selection.Providers;
        }

        List<WebConsoleLoginProvider> providers = [.. selection.Providers];
        foreach (var provider in _globalProviders)
        {
            if (selection.ProviderIds.Contains(provider.Id))
            {
                continue;
            }

            providers.Add(provider);
        }

        return providers;
    }

    public WebConsoleLoginProvider ResolveProvider(string? providerId, string? owner = null)
    {
        var providers = GetProviders(owner);
        if (providers.Count == 0)
        {
            return DefaultProvider;
        }

        if (!string.IsNullOrWhiteSpace(providerId))
        {
            foreach (var provider in providers)
            {
                if (string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase))
                {
                    return provider;
                }
            }
        }

        return providers[0];
    }

    private static IReadOnlyList<WebConsoleLoginProvider> BuildGlobalProviders(
        WebConsoleAuthenticationOptions options,
        string challengeParameterName)
    {
        if (options.Providers.Count > 0)
        {
            return [.. options.Providers.Select(provider => CreateProvider(provider.Key))];
        }

        return string.Equals(challengeParameterName, "connector_id", StringComparison.Ordinal)
            ?
            [
                new("makana", "MK", "Makana account", "Sign in with Makana", "makana")
            ]
            :
            [
                new("passkey", "PK", "Pocket ID passkey", "Sign in with a passkey", "passkey"),
                new("google", "G", "Google", "Sign in with Google", "google"),
                new("microsoft", "MS", "Microsoft", "Sign in with Microsoft", "microsoft"),
                new("github", "GH", "GitHub", "Sign in with GitHub", "github"),
                new("apple", "A", "Apple", "Sign in with Apple", "apple")
            ];
    }

    private static WebConsoleLoginProvider CreateProvider(string providerId)
    {
        var displayName = HumanizeProviderId(providerId);
        var badge = new string([.. displayName
            .Where(char.IsLetterOrDigit)
            .Take(2)])
            .ToUpperInvariant();

        if (string.IsNullOrWhiteSpace(badge))
        {
            badge = "ID";
        }

        return new WebConsoleLoginProvider(
            providerId,
            badge,
            displayName,
            $"Sign in with {displayName}",
            providerId);
    }

    private static string HumanizeProviderId(string providerId)
    {
        var tokens = providerId
            .Split(['-', '_', '.', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant()));

        return string.Join(' ', tokens);
    }

    private static Dictionary<string, OrganizationProviderSelection> BuildOrganizationProviders(
        WebConsoleAuthenticationOptions options,
        IReadOnlyList<WebConsoleLoginProvider> globalProviders)
    {
        Dictionary<string, OrganizationProviderSelection> organizations = new(StringComparer.OrdinalIgnoreCase);

        foreach (var organization in options.Organizations)
        {
            var providerIds = new HashSet<string>(organization.Value.Providers, StringComparer.OrdinalIgnoreCase);
            var providers = globalProviders
                .Where(provider => providerIds.Contains(provider.Id))
                .ToArray();

            organizations[organization.Key] = new OrganizationProviderSelection(
                providers,
                providerIds,
                organization.Value.AllowGlobalProviders);
        }

        return organizations;
    }

    private sealed record OrganizationProviderSelection(
        IReadOnlyList<WebConsoleLoginProvider> Providers,
        HashSet<string> ProviderIds,
        bool AllowGlobalProviders);
}
