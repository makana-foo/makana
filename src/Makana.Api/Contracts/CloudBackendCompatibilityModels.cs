namespace Makana.Api.Contracts;

public sealed class MissingEnvVarForNonInteractiveException(string varName)
    : InvalidOperationException($"missing environment variable for non-interactive login: {varName}")
{
    public string Var { get; } = varName;
}

public static class CloudBackendDisplayFormatter
{
    public static string FormatStackReference(CloudBackendReference reference, bool fullyQualifyStackNames)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return fullyQualifyStackNames ? reference.ToFullyQualifiedString() : reference.ToNameString();
    }
}

public static class CloudBackendLoginPolicy
{
    public const string AccessTokenEnvVar = "PULUMI_ACCESS_TOKEN";

    public static void EnsureAccessToken(string? accessToken, bool disableInteractive)
    {
        if (disableInteractive && string.IsNullOrWhiteSpace(accessToken))
        {
            throw new MissingEnvVarForNonInteractiveException(AccessTokenEnvVar);
        }
    }
}

public static class CloudBackendUrlResolver
{
    public const string DefaultCloudUrl = "https://api.pulumi.com";

    public static string ValueOrDefault(string explicitValue, string? environmentUrl, string? currentCredentialsUrl)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue))
        {
            return explicitValue.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(environmentUrl))
        {
            return environmentUrl;
        }

        if (!string.IsNullOrWhiteSpace(currentCredentialsUrl) && IsHttpUrl(currentCredentialsUrl))
        {
            return currentCredentialsUrl;
        }

        return DefaultCloudUrl;
    }

    private static bool IsHttpUrl(string url)
        => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
           url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

public static class DefaultOrganizationResolver
{
    public static string Infer(Func<string?> getDefaultOrganization, Func<string?> getUserOrganization)
    {
        ArgumentNullException.ThrowIfNull(getDefaultOrganization);
        ArgumentNullException.ThrowIfNull(getUserOrganization);

        Exception? defaultOrgError = null;
        string? defaultOrg = null;
        try
        {
            defaultOrg = getDefaultOrganization();
        }
        catch (Exception ex)
        {
            defaultOrgError = ex;
        }

        if (!string.IsNullOrWhiteSpace(defaultOrg))
        {
            return defaultOrg;
        }

        try
        {
            var userOrg = getUserOrganization();
            if (!string.IsNullOrWhiteSpace(userOrg))
            {
                return userOrg;
            }
        }
        catch when (defaultOrgError is null)
        {
            throw;
        }

        if (defaultOrgError is not null)
        {
            throw defaultOrgError;
        }

        throw new InvalidOperationException("no organization available");
    }
}

public sealed record IntegrityCheckedResource(
    string Urn,
    string? Parent = null);

public static class SnapshotIntegrityReader
{
    public static IReadOnlyList<IntegrityCheckedResource> Read(
        IReadOnlyList<IntegrityCheckedResource> resources,
        bool disableIntegrityChecking)
    {
        ArgumentNullException.ThrowIfNull(resources);

        if (disableIntegrityChecking)
        {
            return resources;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            if (!string.IsNullOrWhiteSpace(resource.Parent) && !seen.Contains(resource.Parent))
            {
                throw new InvalidOperationException(
                    $"child resource {resource.Urn}'s parent {resource.Parent} comes after it");
            }

            seen.Add(resource.Urn);
        }

        return resources;
    }
}

public sealed class CloudRegistry;

public static class CloudRegistryFactory
{
    public static CloudRegistry Create()
        => new();
}

public sealed record CloudBackendStackSummary(CloudBackendReference Name);

public sealed record StackListPage<TItem>(
    IReadOnlyList<TItem> Items,
    string? ContinuationToken);

public static class StackListingCompatibility
{
    public static IReadOnlyList<TItem> CollectPages<TItem>(Func<string?, StackListPage<TItem>> listPage, int maxPages)
    {
        ArgumentNullException.ThrowIfNull(listPage);
        if (maxPages <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPages));
        }

        var results = new List<TItem>();
        string? token = null;
        for (var page = 0; page < maxPages; page++)
        {
            var result = listPage(token);
            results.AddRange(result.Items);
            if (string.IsNullOrWhiteSpace(result.ContinuationToken))
            {
                break;
            }

            token = result.ContinuationToken;
        }

        return results;
    }

    public static bool ContainsStackName(IEnumerable<CloudBackendReference> references, string expectedStackName)
    {
        ArgumentNullException.ThrowIfNull(references);

        return references.Any(reference =>
            string.Equals(reference.StackName, expectedStackName, StringComparison.Ordinal) ||
            string.Equals(reference.ToFullyQualifiedString(), expectedStackName, StringComparison.Ordinal) ||
            string.Equals(reference.ToString(), expectedStackName, StringComparison.Ordinal));
    }

    public static bool ContainsStackName(IEnumerable<CloudBackendStackSummary> summaries, string expectedStackName)
    {
        ArgumentNullException.ThrowIfNull(summaries);

        return summaries.Any(summary => ContainsStackName([summary.Name], expectedStackName));
    }
}

public sealed record DeploymentEnvelope(
    int Version,
    IReadOnlyList<string> Features,
    string DeploymentJson);

public static class DeploymentSchemaVersionCompatibility
{
    public static DeploymentEnvelope Adjust(DeploymentEnvelope deployment, int maxSupportedVersion)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        if (deployment.Version <= maxSupportedVersion)
        {
            return deployment;
        }

        return deployment with
        {
            Version = maxSupportedVersion,
            Features = [],
        };
    }
}
