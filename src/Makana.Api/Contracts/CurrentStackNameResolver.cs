namespace Makana.Api.Contracts;

public static class CurrentStackNameResolver
{
    public static string Resolve(
        string selectedStackName,
        bool backendSupportsOrganizations,
        string? currentUserLogin,
        string? defaultOrganization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedStackName);

        if (selectedStackName.Contains('/', StringComparison.Ordinal))
        {
            return selectedStackName;
        }

        if (!backendSupportsOrganizations || !string.IsNullOrWhiteSpace(defaultOrganization))
        {
            return selectedStackName;
        }

        if (string.IsNullOrWhiteSpace(currentUserLogin))
        {
            return selectedStackName;
        }

        return $"{currentUserLogin}/{selectedStackName}";
    }
}
