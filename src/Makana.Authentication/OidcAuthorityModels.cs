using System.Collections.Concurrent;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;

namespace Makana.Authentication;

internal sealed record OidcAuthorityRegistration(string Name);

internal sealed class OidcLifecycleHealthCheckRegistrationMarker;

internal sealed record ResolvedOidcAuthorityOptions(
    string Authority,
    string MetadataAddress,
    Uri? BackchannelBaseUri,
    bool RequireHttpsMetadata,
    TimeSpan StartupWarmupTimeout,
    TimeSpan BackgroundRefreshInterval,
    TimeSpan AutomaticRefreshInterval,
    TimeSpan HotPathRefreshMinimumInterval,
    TimeSpan LastKnownGoodLifetime);

internal sealed class OidcAuthorityStatus
{
    public DateTimeOffset? LastSuccessfulRefreshUtc { get; private set; }

    public DateTimeOffset? LastFailureUtc { get; private set; }

    public string? LastFailureMessage { get; private set; }

    public DateTimeOffset NextScheduledRefreshUtc { get; set; }

    public void MarkSuccess(DateTimeOffset now)
    {
        LastSuccessfulRefreshUtc = now;
        LastFailureUtc = null;
        LastFailureMessage = null;
    }

    public void MarkFailure(DateTimeOffset now, Exception exception)
    {
        LastFailureUtc = now;
        LastFailureMessage = exception.Message;
    }
}

internal sealed class OidcAuthorityRuntime(
    string name,
    ResolvedOidcAuthorityOptions options,
    ConfigurationManager<OpenIdConnectConfiguration> configurationManager)
{
    public string Name { get; } = name;

    public ResolvedOidcAuthorityOptions Options { get; } = options;

    public ConfigurationManager<OpenIdConnectConfiguration> ConfigurationManager { get; } = configurationManager;

    public OidcAuthorityStatus Status { get; } = new();

    public SemaphoreSlim RefreshLock { get; } = new(1, 1);
}
