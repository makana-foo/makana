using System.Text.Json;
using System.Text.Json.Serialization;

namespace Makana.WebConsole.Models;

public sealed record ServiceUserInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("githubLogin")] string GitHubLogin,
    [property: JsonPropertyName("avatarUrl")] string AvatarUrl,
    [property: JsonPropertyName("email")] string? Email
);

public sealed record ServiceUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("githubLogin")] string GitHubLogin,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("avatarUrl")] string AvatarUrl,
    [property: JsonPropertyName("organizations")] ServiceUserInfo[] Organizations
);

public sealed record StackSummary(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("stackName")] string StackName,
    [property: JsonPropertyName("projectName")] string ProjectName,
    [property: JsonPropertyName("orgName")] string OrgName,
    [property: JsonPropertyName("lastUpdate")] long? LastUpdate,
    [property: JsonPropertyName("resourceCount")] int? ResourceCount,
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags
);

public sealed record StackDetails(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("orgName")] string OrgName,
    [property: JsonPropertyName("projectName")] string ProjectName,
    [property: JsonPropertyName("stackName")] string StackName,
    [property: JsonPropertyName("activeUpdate")] string ActiveUpdate,
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags,
    [property: JsonPropertyName("version")] int Version
);

public sealed record ListStacksResponse(
    [property: JsonPropertyName("stacks")] StackSummary[] Stacks,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken
);

public sealed record UpdateInfo(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("startTime")] long? StartTime,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("result")] string? Result,
    [property: JsonPropertyName("endTime")] long? EndTime,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("updateID")] string? UpdateId
);

public sealed record GetHistoryResponse(
    [property: JsonPropertyName("updates")] UpdateInfo[] Updates
);

public sealed record LatestUpdateInfo(
    [property: JsonPropertyName("info")] UpdateInfo Info
);

public sealed record UpdateResults(
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("version")] int? Version
);

public sealed record GetUpdateEventsResponse(
    [property: JsonPropertyName("events")] JsonElement[] Events,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken
);
