using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Makana.Authentication;

internal sealed class OidcAuthorityRegistry(
    IEnumerable<OidcAuthorityRegistration> registrations,
    IOptionsMonitor<MakanaOidcAuthorityOptions> optionsMonitor,
    IHttpClientFactory httpClientFactory,
    ILoggerFactory loggerFactory)
{
    private readonly string[] _registrationNames = registrations
        .Select(static registration => registration.Name)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private readonly ConcurrentDictionary<string, OidcAuthorityRuntime> _authorities = new(StringComparer.Ordinal);

    public IReadOnlyList<OidcAuthorityRuntime> GetAuthorities()
        => _registrationNames.Select(GetAuthority).ToArray();

    public OidcAuthorityRuntime GetAuthority(string name)
        => _authorities.GetOrAdd(name, static (authorityName, state) => state.CreateRuntime(authorityName), this);

    public async Task WarmupAllAsync(CancellationToken cancellationToken)
    {
        foreach (var authority in GetAuthorities())
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(authority.Options.StartupWarmupTimeout);
            await RefreshAuthorityAsync(authority, requestRefresh: false, timeoutCts.Token);
            authority.Status.NextScheduledRefreshUtc = DateTimeOffset.UtcNow + authority.Options.BackgroundRefreshInterval + RandomJitter();
        }
    }

    public async Task RefreshScheduledAuthoritiesAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var authority in GetAuthorities())
        {
            if (authority.Status.NextScheduledRefreshUtc > now)
            {
                continue;
            }

            await RefreshAuthorityAsync(authority, requestRefresh: true, cancellationToken);
            authority.Status.NextScheduledRefreshUtc = DateTimeOffset.UtcNow + authority.Options.BackgroundRefreshInterval;
        }
    }

    public async Task RefreshAuthorityAsync(OidcAuthorityRuntime authority, bool requestRefresh, CancellationToken cancellationToken)
    {
        await authority.RefreshLock.WaitAsync(cancellationToken);
        try
        {
            if (requestRefresh)
            {
                authority.ConfigurationManager.RequestRefresh();
            }

            await authority.ConfigurationManager.GetConfigurationAsync(cancellationToken);
            authority.Status.MarkSuccess(DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            authority.Status.MarkFailure(DateTimeOffset.UtcNow, ex);
            throw;
        }
        finally
        {
            authority.RefreshLock.Release();
        }
    }

    private OidcAuthorityRuntime CreateRuntime(string name)
    {
        var options = ResolveOptions(optionsMonitor.Get(name));
        var retriever = new HttpDocumentRetriever(httpClientFactory.CreateClient(MakanaOidcDefaults.ConfigurationHttpClientName))
        {
            RequireHttps = options.RequireHttpsMetadata
        };
        var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            options.MetadataAddress,
            new RewritingOpenIdConnectConfigurationRetriever(options),
            retriever)
        {
            AutomaticRefreshInterval = options.AutomaticRefreshInterval,
            RefreshInterval = options.HotPathRefreshMinimumInterval,
            UseLastKnownGoodConfiguration = true,
            LastKnownGoodLifetime = options.LastKnownGoodLifetime
        };

        var runtime = new OidcAuthorityRuntime(name, options, configurationManager);
        var logger = loggerFactory.CreateLogger<OidcAuthorityRegistry>();
        logger.LogInformation(
            "Registered OIDC authority {AuthorityName} with metadata {MetadataAddress}.",
            name,
            options.MetadataAddress);

        return runtime;
    }

    private static ResolvedOidcAuthorityOptions ResolveOptions(MakanaOidcAuthorityOptions options)
    {
        var authority = NormalizeAuthority(options.Authority);
        var metadataAddress = string.IsNullOrWhiteSpace(options.MetadataAddress)
            ? $"{authority}/.well-known/openid-configuration"
            : options.MetadataAddress;

        return new ResolvedOidcAuthorityOptions(
            authority,
            metadataAddress,
            string.IsNullOrWhiteSpace(options.BackchannelBaseUrl)
                ? null
                : new Uri(options.BackchannelBaseUrl, UriKind.Absolute),
            options.RequireHttpsMetadata,
            options.StartupWarmupTimeout,
            options.BackgroundRefreshInterval,
            options.AutomaticRefreshInterval,
            options.HotPathRefreshMinimumInterval,
            options.LastKnownGoodLifetime);
    }

    private static string NormalizeAuthority(string authority)
        => authority.Trim().TrimEnd('/');

    private static TimeSpan RandomJitter()
        => TimeSpan.FromSeconds(Random.Shared.Next(0, 300));
}
