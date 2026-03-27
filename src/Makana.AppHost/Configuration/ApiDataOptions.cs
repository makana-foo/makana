namespace Makana.AppHost.Configuration;

internal sealed class ApiDataOptions
{
    public const string SectionName = "ApiData";

    public bool UsePersistentState { get; init; } = true;
}
