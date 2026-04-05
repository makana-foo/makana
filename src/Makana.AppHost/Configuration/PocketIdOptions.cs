namespace Makana.AppHost.Configuration;

internal sealed class PocketIdOptions
{
    public const string SectionName = "PocketId";

    public bool UsePersistentState { get; init; } = true;
    public string? DataDirectory { get; init; }
    public string? VolumeName { get; init; }

    public string ResolveDataDirectory(string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(DataDirectory))
        {
            return Path.GetFullPath(DataDirectory);
        }

        return Path.Join(contentRootPath, ".data", "pocketid");
    }
}
