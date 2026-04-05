namespace Makana.Authentication;

public sealed class MakanaOidcAuthorityOptions
{
    public string Authority { get; set; } = string.Empty;

    public string MetadataAddress { get; set; } = string.Empty;

    public string BackchannelBaseUrl { get; set; } = string.Empty;

    public bool RequireHttpsMetadata { get; set; } = true;

    public TimeSpan StartupWarmupTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan BackgroundRefreshInterval { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan AutomaticRefreshInterval { get; set; } = TimeSpan.FromHours(12);

    public TimeSpan HotPathRefreshMinimumInterval { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan LastKnownGoodLifetime { get; set; } = TimeSpan.FromHours(24);
}
