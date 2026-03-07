using System.Text.Json;
using System.Text.Json.Serialization;

namespace Makana.StackUpdates.Pulumi;

public sealed record CancelEvent;

public sealed record StdoutEngineEvent(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("color")] string? Color);

public sealed record DiagnosticEvent(
    [property: JsonPropertyName("urn")] string? Urn,
    [property: JsonPropertyName("prefix")] string? Prefix,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("color")] string? Color,
    [property: JsonPropertyName("severity")] string Severity,
    [property: JsonPropertyName("streamID")] int? StreamId,
    [property: JsonPropertyName("ephemeral")] bool? Ephemeral);

public sealed record StartDebuggingEvent(
    [property: JsonPropertyName("config")] Dictionary<string, JsonElement>? Config);

public sealed record PolicyEvent(
    [property: JsonPropertyName("resourceUrn")] string? ResourceUrn,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("color")] string? Color,
    [property: JsonPropertyName("policyName")] string PolicyName,
    [property: JsonPropertyName("policyPackName")] string PolicyPackName,
    [property: JsonPropertyName("policyPackVersion")] string PolicyPackVersion,
    [property: JsonPropertyName("policyPackVersionTag")] string PolicyPackVersionTag,
    [property: JsonPropertyName("enforcementLevel")] string EnforcementLevel,
    [property: JsonPropertyName("severity")] string? Severity);

public sealed record PolicyRemediationEvent(
    [property: JsonPropertyName("resourceUrn")] string? ResourceUrn,
    [property: JsonPropertyName("color")] string? Color,
    [property: JsonPropertyName("policyName")] string PolicyName,
    [property: JsonPropertyName("policyPackName")] string PolicyPackName,
    [property: JsonPropertyName("policyPackVersion")] string PolicyPackVersion,
    [property: JsonPropertyName("policyPackVersionTag")] string PolicyPackVersionTag,
    [property: JsonPropertyName("before")] Dictionary<string, JsonElement>? Before,
    [property: JsonPropertyName("after")] Dictionary<string, JsonElement>? After);

public sealed record PolicyAnalyzeSummaryEvent(
    [property: JsonPropertyName("resourceUrn")] string ResourceUrn,
    [property: JsonPropertyName("policyPackName")] string PolicyPackName,
    [property: JsonPropertyName("policyPackVersion")] string PolicyPackVersion,
    [property: JsonPropertyName("policyPackVersionTag")] string PolicyPackVersionTag,
    [property: JsonPropertyName("passed")] string[]? Passed,
    [property: JsonPropertyName("failed")] string[]? Failed);

public sealed record PolicyRemediateSummaryEvent(
    [property: JsonPropertyName("resourceUrn")] string ResourceUrn,
    [property: JsonPropertyName("policyPackName")] string PolicyPackName,
    [property: JsonPropertyName("policyPackVersion")] string PolicyPackVersion,
    [property: JsonPropertyName("policyPackVersionTag")] string PolicyPackVersionTag,
    [property: JsonPropertyName("passed")] string[]? Passed,
    [property: JsonPropertyName("failed")] string[]? Failed);

public sealed record PolicyAnalyzeStackSummaryEvent(
    [property: JsonPropertyName("policyPackName")] string PolicyPackName,
    [property: JsonPropertyName("policyPackVersion")] string PolicyPackVersion,
    [property: JsonPropertyName("policyPackVersionTag")] string PolicyPackVersionTag,
    [property: JsonPropertyName("passed")] string[]? Passed,
    [property: JsonPropertyName("failed")] string[]? Failed);

public sealed record PreludeEvent(
    [property: JsonPropertyName("config")] Dictionary<string, string> Config);

public sealed record SummaryEvent(
    [property: JsonPropertyName("maybeCorrupt")] bool MaybeCorrupt,
    [property: JsonPropertyName("durationSeconds")] int DurationSeconds,
    [property: JsonPropertyName("resourceChanges")] Dictionary<string, int> ResourceChanges,
    [property: JsonPropertyName("PolicyPacks")] Dictionary<string, string>? PolicyPacks,
    [property: JsonPropertyName("isPreview")] bool IsPreview);

public sealed record ErrorEvent(
    [property: JsonPropertyName("error")] string Error);

public sealed record PropertyDiff(
    [property: JsonPropertyName("diffKind")] string DiffKind,
    [property: JsonPropertyName("inputDiff")] bool InputDiff);

public sealed record StepEventMetadata(
    [property: JsonPropertyName("op")] string Op,
    [property: JsonPropertyName("urn")] string? Urn,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("old")] StepEventStateMetadata? Old,
    [property: JsonPropertyName("new")] StepEventStateMetadata? New,
    [property: JsonPropertyName("keys")] string[]? Keys,
    [property: JsonPropertyName("diffs")] string[]? Diffs,
    [property: JsonPropertyName("detailedDiff")] Dictionary<string, PropertyDiff>? DetailedDiff,
    [property: JsonPropertyName("logical")] bool? Logical,
    [property: JsonPropertyName("provider")] string? Provider);

public sealed record StepEventStateMetadata(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("urn")] string? Urn,
    [property: JsonPropertyName("custom")] bool? Custom,
    [property: JsonPropertyName("delete")] bool? Delete,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("parent")] string? Parent,
    [property: JsonPropertyName("protect")] bool? Protect,
    [property: JsonPropertyName("taint")] bool? Taint,
    [property: JsonPropertyName("retainOnDelete")] bool? RetainOnDelete,
    [property: JsonPropertyName("inputs")] Dictionary<string, JsonElement>? Inputs,
    [property: JsonPropertyName("outputs")] Dictionary<string, JsonElement>? Outputs,
    [property: JsonPropertyName("provider")] string? Provider,
    [property: JsonPropertyName("initErrors")] string[]? InitErrors,
    [property: JsonPropertyName("hideDiffs")] string[]? HideDiffs);

public sealed record ResourcePreEvent(
    [property: JsonPropertyName("metadata")] StepEventMetadata Metadata,
    [property: JsonPropertyName("planning")] bool? Planning);

public sealed record ResOutputsEvent(
    [property: JsonPropertyName("metadata")] StepEventMetadata Metadata,
    [property: JsonPropertyName("planning")] bool? Planning);

public sealed record ResOpFailedEvent(
    [property: JsonPropertyName("metadata")] StepEventMetadata Metadata,
    [property: JsonPropertyName("status")] int Status,
    [property: JsonPropertyName("steps")] int Steps);

public sealed record PolicyLoadEvent;

public sealed record ProgressEvent(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("received")] long Completed,
    [property: JsonPropertyName("total")] long Total,
    [property: JsonPropertyName("done")] bool Done);

public sealed record EngineEvent(
    [property: JsonPropertyName("sequence")] int Sequence,
    [property: JsonPropertyName("timestamp")] int Timestamp,
    [property: JsonPropertyName("cancelEvent")] CancelEvent? CancelEvent = null,
    [property: JsonPropertyName("stdoutEvent")] StdoutEngineEvent? StdoutEvent = null,
    [property: JsonPropertyName("diagnosticEvent")] DiagnosticEvent? DiagnosticEvent = null,
    [property: JsonPropertyName("preludeEvent")] PreludeEvent? PreludeEvent = null,
    [property: JsonPropertyName("summaryEvent")] SummaryEvent? SummaryEvent = null,
    [property: JsonPropertyName("resourcePreEvent")] ResourcePreEvent? ResourcePreEvent = null,
    [property: JsonPropertyName("resOutputsEvent")] ResOutputsEvent? ResOutputsEvent = null,
    [property: JsonPropertyName("resOpFailedEvent")] ResOpFailedEvent? ResOpFailedEvent = null,
    [property: JsonPropertyName("policyEvent")] PolicyEvent? PolicyEvent = null,
    [property: JsonPropertyName("policyRemediationEvent")] PolicyRemediationEvent? PolicyRemediationEvent = null,
    [property: JsonPropertyName("policyLoadEvent")] PolicyLoadEvent? PolicyLoadEvent = null,
    [property: JsonPropertyName("policyAnalyzeSummaryEvent")] PolicyAnalyzeSummaryEvent? PolicyAnalyzeSummaryEvent = null,
    [property: JsonPropertyName("policyRemediateSummaryEvent")] PolicyRemediateSummaryEvent? PolicyRemediateSummaryEvent = null,
    [property: JsonPropertyName("policyAnalyzeStackSummaryEvent")] PolicyAnalyzeStackSummaryEvent? PolicyAnalyzeStackSummaryEvent = null,
    [property: JsonPropertyName("startDebuggingEvent")] StartDebuggingEvent? StartDebuggingEvent = null,
    [property: JsonPropertyName("progressEvent")] ProgressEvent? ProgressEvent = null,
    [property: JsonPropertyName("errorEvent")] ErrorEvent? ErrorEvent = null)
{
    [JsonIgnore]
    public int PayloadCount =>
        (CancelEvent is null ? 0 : 1) +
        (StdoutEvent is null ? 0 : 1) +
        (DiagnosticEvent is null ? 0 : 1) +
        (PreludeEvent is null ? 0 : 1) +
        (SummaryEvent is null ? 0 : 1) +
        (ResourcePreEvent is null ? 0 : 1) +
        (ResOutputsEvent is null ? 0 : 1) +
        (ResOpFailedEvent is null ? 0 : 1) +
        (PolicyEvent is null ? 0 : 1) +
        (PolicyRemediationEvent is null ? 0 : 1) +
        (PolicyLoadEvent is null ? 0 : 1) +
        (PolicyAnalyzeSummaryEvent is null ? 0 : 1) +
        (PolicyRemediateSummaryEvent is null ? 0 : 1) +
        (PolicyAnalyzeStackSummaryEvent is null ? 0 : 1) +
        (StartDebuggingEvent is null ? 0 : 1) +
        (ProgressEvent is null ? 0 : 1) +
        (ErrorEvent is null ? 0 : 1);

    [JsonIgnore]
    public bool HasSinglePayload => PayloadCount == 1;
}

public sealed record EngineEventBatch(
    [property: JsonPropertyName("events")] EngineEvent[] Events);

public sealed record GetUpdateEventsResponse(
    [property: JsonPropertyName("events")] EngineEvent[] Events,
    [property: JsonPropertyName("continuationToken")] string? ContinuationToken);
