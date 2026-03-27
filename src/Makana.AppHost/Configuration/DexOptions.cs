namespace Makana.AppHost.Configuration;

internal sealed class DexOptions
{
    public const string SectionName = "Dex";

    public bool UsePersistentState { get; init; } = true;
    public string? DataDirectory { get; init; }
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
