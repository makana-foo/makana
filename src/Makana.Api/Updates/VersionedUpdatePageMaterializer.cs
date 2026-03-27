using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Endpoints;
using Makana.Api.Storage;
using Makana.StackUpdates.Pulumi;

namespace Makana.Api.Updates;

internal sealed class VersionedUpdatePageMaterializer(
    UpdateQueryRepository updateQueries,
    UpdateLogRepository updateLog,
    EndpointHelpers endpointHelpers)
{
    public async Task<UpdateServiceEnvelope?> BuildServiceEnvelopeAsync(
        string stackId,
        string updateId,
        CancellationToken cancellationToken)
    {
        var update = updateQueries.GetUpdateDetails(stackId, updateId);
        if (update is null)
        {
            return null;
        }

        var latestVersion = await GetLatestVersionAsync(stackId);
        var resourceChanges = await ReadResourceChangesAsync(update.Value.Id, cancellationToken);
        var resourceCount = await ReadResourceCountForVersionAsync(stackId, update.Value.Version, cancellationToken);
        var isPreview = endpointHelpers.IsPreviewOperation(
            update.Value.Kind,
            update.Value.ProgramRequestJson,
            await IsPreviewAsync(update.Value.Id, cancellationToken));
        var (message, environment) = endpointHelpers.ExtractMetadata(update.Value.ProgramRequestJson);
        var info = new UpdateInfo(
            Kind: update.Value.Kind,
            IsPreview: isPreview,
            StartTime: endpointHelpers.ToUnixTimeSeconds(update.Value.CreatedAt) ?? 0,
            Message: message,
            Environment: environment,
            Config: endpointHelpers.ExtractConfig(update.Value.ProgramRequestJson),
            Result: endpointHelpers.NormalizeUpdateResult(update.Value.Status),
            EndTime: endpointHelpers.ToUnixTimeSeconds(update.Value.CompletedAt) ?? 0,
            Version: update.Value.Version,
            ResourceChanges: resourceChanges,
            ResourceCount: resourceCount,
            UpdateId: update.Value.Id);

        var requestedBy = BuildRequestedBy(environment);
        var gitHubCommitInfo = BuildGitHubCommitInfo(environment, requestedBy);

        return new UpdateServiceEnvelope(
            Info: info,
            UpdateId: update.Value.Id,
            GitHubCommitInfo: gitHubCommitInfo,
            Version: update.Value.Version,
            LatestVersion: latestVersion,
            RequestedBy: requestedBy,
            RequestedByToken: string.Empty);
    }

    public async Task<bool> IsPreviewAsync(string updateId, CancellationToken cancellationToken)
    {
        var previewFlags = updateQueries.GetUpdatePreviewFlags([updateId]);
        return previewFlags.GetValueOrDefault(updateId);
    }

    public async Task<UpdateServiceEnvelope?> BuildServiceEnvelopeForLatestAsync(
        string stackId,
        CancellationToken cancellationToken)
    {
        var latest = updateQueries.GetHistory(stackId).FirstOrDefault();
        if (latest == default)
        {
            return null;
        }

        return await BuildServiceEnvelopeAsync(stackId, latest.Id, cancellationToken);
    }

    public async Task<UpdateVersionSummary?> BuildVersionSummaryAsync(
        string stackId,
        int version,
        CancellationToken cancellationToken)
    {
        var update = updateQueries.GetUpdateByVersion(stackId, version);
        if (update is null)
        {
            return null;
        }

        var resourceCount = await ReadResourceCountForVersionAsync(stackId, version, cancellationToken) ?? 0;
        return new UpdateVersionSummary(
            Result: endpointHelpers.NormalizeUpdateResult(update.Value.Status),
            StartTime: endpointHelpers.ToUnixTimeSeconds(update.Value.CreatedAt) ?? 0,
            EndTime: endpointHelpers.ToUnixTimeSeconds(update.Value.CompletedAt) ?? 0,
            ResourceCount: resourceCount);
    }

    public async Task<StackResourceCountResponse?> BuildResourceCountAsync(
        string stackId,
        CancellationToken cancellationToken)
    {
        var latestDeployment = updateQueries.GetLatestDeployment(stackId);
        if (latestDeployment is null)
        {
            return null;
        }

        var resources = ParseResources(latestDeployment.Value.DeploymentJson);
        return new StackResourceCountResponse(resources.Length, latestDeployment.Value.Version);
    }

    public async Task<StackResourcesResponse?> BuildLatestResourcesAsync(
        string stackId,
        CancellationToken cancellationToken)
    {
        var latestDeployment = updateQueries.GetLatestDeployment(stackId);
        if (latestDeployment is null)
        {
            return null;
        }

        return BuildResourcesResponse(latestDeployment.Value.Version, latestDeployment.Value.DeploymentJson);
    }

    public async Task<StackResourcesResponse?> BuildResourcesForVersionAsync(
        string stackId,
        int version,
        CancellationToken cancellationToken)
    {
        var deployment = updateQueries.GetDeploymentByVersion(stackId, version);
        if (deployment is null)
        {
            return null;
        }

        return BuildResourcesResponse(deployment.Value.Version, deployment.Value.DeploymentJson);
    }

    private async Task<int> GetLatestVersionAsync(string stackId)
    {
        var latest = updateQueries.GetHistory(stackId).FirstOrDefault();
        return latest == default ? 0 : latest.Version;
    }

    private async Task<Dictionary<string, int>?> ReadResourceChangesAsync(
        string updateId,
        CancellationToken cancellationToken)
    {
        var events = updateLog.GetEvents(updateId);
        foreach (var eventJson in events.AsEnumerable().Reverse())
        {
            var envelope = JsonSerializer.Deserialize(eventJson, PulumiEngineEventJsonContext.Default.EngineEvent);
            if (envelope?.SummaryEvent?.ResourceChanges is { Count: > 0 } resourceChanges)
            {
                return new Dictionary<string, int>(resourceChanges, StringComparer.Ordinal);
            }
        }

        return null;
    }

    private async Task<int?> ReadResourceCountForVersionAsync(
        string stackId,
        int version,
        CancellationToken cancellationToken)
    {
        var deployment = updateQueries.GetDeploymentByVersion(stackId, version);
        if (deployment is null)
        {
            return null;
        }

        return ParseResources(deployment.Value.DeploymentJson).Length;
    }

    private static ServiceUserInfo? BuildRequestedBy(Dictionary<string, string>? environment)
    {
        if (environment is null)
        {
            return null;
        }

        environment.TryGetValue("git.author", out var authorName);
        environment.TryGetValue("vcs.owner", out var vcsOwner);
        environment.TryGetValue("git.author.email", out var authorEmail);

        if (string.IsNullOrWhiteSpace(authorName) && string.IsNullOrWhiteSpace(vcsOwner))
        {
            return null;
        }

        return new ServiceUserInfo(
            Name: authorName ?? vcsOwner ?? string.Empty,
            GitHubLogin: vcsOwner ?? string.Empty,
            AvatarUrl: string.Empty,
            Email: authorEmail);
    }

    private static UpdateServiceGitHubCommitInfo? BuildGitHubCommitInfo(
        Dictionary<string, string>? environment,
        ServiceUserInfo? requestedBy)
    {
        if (environment is null)
        {
            return null;
        }

        environment.TryGetValue("vcs.owner", out var owner);
        environment.TryGetValue("vcs.repo", out var repo);
        environment.TryGetValue("git.head", out var sha);
        if (string.IsNullOrWhiteSpace(owner) ||
            string.IsNullOrWhiteSpace(repo) ||
            string.IsNullOrWhiteSpace(sha))
        {
            return null;
        }

        var author = requestedBy is null
            ? null
            : new UpdateServiceGitActor(
                requestedBy.Name,
                requestedBy.GitHubLogin,
                requestedBy.AvatarUrl);

        return new UpdateServiceGitHubCommitInfo(
            Slug: $"{owner}/{repo}",
            Sha: sha,
            Url: $"https://github.com/{owner}/{repo}/commit/{sha}",
            Author: author);
    }

    private StackResourcesResponse BuildResourcesResponse(int version, string deploymentJson)
    {
        var resources = ParseResources(deploymentJson);
        var region = TryReadRegion(resources);
        return new StackResourcesResponse(resources, region, version);
    }

    private static StackResourceRecord[] ParseResources(string deploymentJson)
    {
        using var document = JsonDocument.Parse(deploymentJson);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("deployment", out var deploymentElement) &&
            deploymentElement.ValueKind == JsonValueKind.Object)
        {
            root = deploymentElement;
        }

        if (!root.TryGetProperty("resources", out var resourcesElement) ||
            resourcesElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var resources = new List<StackResourceRecord>();
        foreach (var resourceElement in resourcesElement.EnumerateArray())
        {
            resources.Add(new StackResourceRecord(resourceElement.Clone()));
        }

        return [.. resources];
    }

    private static string? TryReadRegion(IEnumerable<StackResourceRecord> resources)
    {
        foreach (var resource in resources)
        {
            if (resource.Resource.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (resource.Resource.TryGetProperty("outputs", out var outputs) &&
                outputs.ValueKind == JsonValueKind.Object &&
                outputs.TryGetProperty("region", out var outputRegion) &&
                outputRegion.ValueKind == JsonValueKind.String)
            {
                return outputRegion.GetString();
            }

            if (resource.Resource.TryGetProperty("providerInputs", out var providerInputs) &&
                providerInputs.ValueKind == JsonValueKind.Object &&
                providerInputs.TryGetProperty("region", out var providerRegion) &&
                providerRegion.ValueKind == JsonValueKind.String)
            {
                return providerRegion.GetString();
            }
        }

        return null;
    }
}
