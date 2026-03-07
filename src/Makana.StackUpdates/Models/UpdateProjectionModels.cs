using Makana.StackUpdates.Pulumi;

namespace Makana.StackUpdates.Models;

public enum UpdateActivityKind
{
    Resource,
    Diagnostic,
    Stdout,
    Summary,
    Unknown
}

public sealed record UpdateProjection(
    long LastSequence,
    bool? IsPreview,
    UpdateConfigProjection? Config,
    UpdateStackOutputsProjection? StackOutputs,
    IReadOnlyList<UpdateResourceProjection> Resources,
    IReadOnlyList<UpdateDiagnosticProjection> Diagnostics,
    IReadOnlyList<UpdateStdoutProjection> Stdout,
    UpdateSummaryProjection? Summary,
    IReadOnlyList<UpdateActivityProjection> Activity);

public sealed record UpdateConfigProjection(
    long Sequence,
    IReadOnlyList<KeyValuePair<string, string>> Entries,
    string RawPayloadJson);

public sealed record UpdateStackOutputsProjection(
    long Sequence,
    IReadOnlyDictionary<string, string?>? OldOutputs,
    IReadOnlyDictionary<string, string?>? NewOutputs);

public sealed record UpdateResourceProjection(
    string Key,
    long FirstSequence,
    long Sequence,
    string Marker,
    string Operation,
    string? ResourceUrn,
    string? ParentUrn,
    string? ResourceType,
    string? ResourceName,
    string? Status,
    string? Info,
    StepEventMetadata Metadata,
    string RawPayloadJson,
    bool IsError);

public sealed record UpdateDiagnosticProjection(
    string Key,
    long Sequence,
    string Severity,
    string? ResourceUrn,
    string Message,
    DiagnosticEvent Event,
    string RawPayloadJson,
    bool IsError);

public sealed record UpdateStdoutProjection(
    string Key,
    long Sequence,
    string Channel,
    string Text,
    StdoutEngineEvent Event,
    string RawPayloadJson,
    bool IsError);

public sealed record UpdateSummaryProjection(
    string Key,
    long Sequence,
    string Summary,
    SummaryEvent Event,
    string RawPayloadJson);

public sealed record UpdateActivityProjection(
    string Key,
    long Sequence,
    UpdateActivityKind Kind,
    string Title,
    string? Detail,
    string? ResourceUrn,
    bool IsError);
