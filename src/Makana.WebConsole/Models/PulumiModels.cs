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

public sealed record ListStacksResponse(
    [property: JsonPropertyName("stacks")] StackSummary[] Stacks,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken
);

public sealed record StackConfigValue(
    [property: JsonPropertyName("string")] string String,
    [property: JsonPropertyName("secret")] bool Secret,
    [property: JsonPropertyName("object")] bool Object
);

public sealed record SelectedUpdateReferenceResponse(
    [property: JsonPropertyName("updateID")] string UpdateId,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("isPreview")] bool IsPreview,
    [property: JsonPropertyName("routeRunType")] string RouteRunType,
    [property: JsonPropertyName("routeReference")] string RouteReference
);

public sealed record UpdateViewInfo(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("startTime")] long? StartTime,
    [property: JsonPropertyName("endTime")] long? EndTime,
    [property: JsonPropertyName("updateID")] string? UpdateId
);

public sealed record UpdateViewConfigEntry(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("secret")] bool Secret,
    [property: JsonPropertyName("object")] bool Object
);

public sealed record UpdateViewOutputEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("oldValue")] string? OldValue,
    [property: JsonPropertyName("newValue")] string? NewValue,
    [property: JsonPropertyName("changed")] bool Changed
);

public sealed record UpdateViewResourceEntry(
    [property: JsonPropertyName("urn")] string? Urn,
    [property: JsonPropertyName("parentUrn")] string? ParentUrn,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("operation")] string Operation,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("info")] string? Info,
    [property: JsonPropertyName("isError")] bool IsError
);

public sealed record UpdateViewSummary(
    [property: JsonPropertyName("isPreview")] bool IsPreview,
    [property: JsonPropertyName("maybeCorrupt")] bool MaybeCorrupt,
    [property: JsonPropertyName("durationSeconds")] long? DurationSeconds,
    [property: JsonPropertyName("resourceChanges")] Dictionary<string, int>? ResourceChanges
);

public sealed record UpdateViewHeader(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("isPreview")] bool IsPreview,
    [property: JsonPropertyName("kindLabel")] string KindLabel,
    [property: JsonPropertyName("runLabel")] string RunLabel,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("startTime")] long? StartTime,
    [property: JsonPropertyName("endTime")] long? EndTime,
    [property: JsonPropertyName("resourceCount")] int? ResourceCount,
    [property: JsonPropertyName("resourceChanges")] Dictionary<string, int>? ResourceChanges
);

public sealed record UpdateViewHistoryEntry(
    [property: JsonPropertyName("updateID")] string? UpdateId,
    [property: JsonPropertyName("version")] int? Version,
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("isPreview")] bool IsPreview,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("kindLabel")] string KindLabel,
    [property: JsonPropertyName("runLabel")] string RunLabel,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("startTime")] long? StartTime,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("routeRunType")] string? RouteRunType,
    [property: JsonPropertyName("routeReference")] string? RouteReference,
    [property: JsonPropertyName("isSelected")] bool IsSelected
);

public sealed record StackPageMetadata(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("activeUpdate")] string ActiveUpdate,
    [property: JsonPropertyName("tags")] Dictionary<string, string>? Tags
);

public sealed record StackOverviewResponse(
    [property: JsonPropertyName("stack")] StackPageMetadata Stack,
    [property: JsonPropertyName("latest")] UpdateViewHeader? Latest,
    [property: JsonPropertyName("history")] UpdateViewHistoryEntry[] History
);

public sealed record UpdateViewChangeConfigEntry(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("value")] string Value
);

public sealed record UpdateViewChangeEntry(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("isError")] bool IsError,
    [property: JsonPropertyName("marker")] string? Marker,
    [property: JsonPropertyName("channel")] string? Channel,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("resourceUrn")] string? ResourceUrn,
    [property: JsonPropertyName("parentUrn")] string? ParentUrn,
    [property: JsonPropertyName("resourceType")] string? ResourceType,
    [property: JsonPropertyName("resourceName")] string? ResourceName,
    [property: JsonPropertyName("operation")] string? Operation,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("info")] string? Info,
    [property: JsonPropertyName("text")] string? Text,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("configEntries")] UpdateViewChangeConfigEntry[]? ConfigEntries,
    [property: JsonPropertyName("rawPayloadJson")] string? RawPayloadJson
);

public sealed record UpdateViewChanges(
    [property: JsonPropertyName("hasEvents")] bool HasEvents,
    [property: JsonPropertyName("messageCount")] int MessageCount,
    [property: JsonPropertyName("summaryText")] string SummaryText,
    [property: JsonPropertyName("diffText")] string DiffText,
    [property: JsonPropertyName("diagnosticText")] string DiagnosticText,
    [property: JsonPropertyName("entries")] UpdateViewChangeEntry[]? Entries,
    [property: JsonPropertyName("deltaEntries")] UpdateViewChangeEntry[]? DeltaEntries,
    [property: JsonPropertyName("summaryLines")] UpdateViewConsoleLine[]? SummaryLines,
    [property: JsonPropertyName("diffLines")] UpdateViewConsoleLine[]? DiffLines,
    [property: JsonPropertyName("diagnosticLines")] UpdateViewConsoleLine[]? DiagnosticLines,
    [property: JsonPropertyName("summaryLineDeltas")] UpdateViewConsoleLineDelta[]? SummaryLineDeltas,
    [property: JsonPropertyName("diffLineDeltas")] UpdateViewConsoleLineDelta[]? DiffLineDeltas,
    [property: JsonPropertyName("diagnosticLineDeltas")] UpdateViewConsoleLineDelta[]? DiagnosticLineDeltas
);

public sealed record UpdateViewConsoleLine(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cssClass")] string CssClass,
    [property: JsonPropertyName("segments")] UpdateViewConsoleSegment[] Segments
);

public sealed record UpdateViewConsoleSegment(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("cssClass")] string CssClass
);

public sealed record UpdateViewConsoleLineDelta(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("line")] UpdateViewConsoleLine? Line
);

public sealed record UpdateViewDelta(
    [property: JsonPropertyName("isIncremental")] bool IsIncremental,
    [property: JsonPropertyName("infoChanged")] bool InfoChanged,
    [property: JsonPropertyName("changesChanged")] bool ChangesChanged,
    [property: JsonPropertyName("configurationChanged")] bool ConfigurationChanged,
    [property: JsonPropertyName("outputsChanged")] bool OutputsChanged,
    [property: JsonPropertyName("resourcesChanged")] bool ResourcesChanged,
    [property: JsonPropertyName("summaryChanged")] bool SummaryChanged
);

public sealed record UpdateViewResponse(
    [property: JsonPropertyName("stack")] StackPageMetadata Stack,
    [property: JsonPropertyName("info")] UpdateViewInfo Info,
    [property: JsonPropertyName("header")] UpdateViewHeader Header,
    [property: JsonPropertyName("history")] UpdateViewHistoryEntry[] History,
    [property: JsonPropertyName("configuration")] UpdateViewConfigEntry[] Configuration,
    [property: JsonPropertyName("outputs")] UpdateViewOutputEntry[] Outputs,
    [property: JsonPropertyName("resources")] UpdateViewResourceEntry[] Resources,
    [property: JsonPropertyName("summary")] UpdateViewSummary? Summary,
    [property: JsonPropertyName("changes")] UpdateViewChanges Changes,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken,
    [property: JsonPropertyName("delta")] UpdateViewDelta? Delta
);
