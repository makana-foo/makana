namespace Makana.AppHost.Configuration;

internal sealed class DexOptions
{
    public const string SectionName = "Dex";

    public bool UsePersistentState { get; init; } = true;
    public string? DataDirectory { get; init; }
    public string? VolumeName { get; init; }
    public bool SessionsEnabled { get; init; } = true;
    public string SessionCookieName { get; init; } = "dex_session";
    public string SessionAbsoluteLifetime { get; init; } = "24h";
    public string SessionValidIfNotUsedFor { get; init; } = "1h";
    public bool SessionRememberMeCheckedByDefault { get; init; }
    public string SessionSsoSharedWithDefault { get; init; } = "all";
    public string ConnectorId { get; init; } = "makana";
    public string ConnectorName { get; init; } = "Makana";
    public string UpstreamClientId { get; init; } = "makana-dex-upstream";
    public string UpstreamClientName { get; init; } = "Makana Dex Upstream";

    public string ResolveDataDirectory(string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(DataDirectory))
        {
            return Path.GetFullPath(DataDirectory);
        }

        return Path.Join(contentRootPath, ".data", "dex");
    }
}
