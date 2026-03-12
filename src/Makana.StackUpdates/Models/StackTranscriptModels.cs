using System.Text.Json;
using Makana.StackUpdates.Pulumi;

namespace Makana.StackUpdates.Models;

public enum StackTranscriptRowKind
{
    Configuration,
    Resource,
    Diagnostic,
    Text,
    Summary,
    Unknown
}

public enum StackTranscriptRowUpdateKind
{
    Append,
    Upsert
}

public abstract record StackTranscriptRow(
    string Key,
    StackTranscriptRowKind Kind,
    long Sequence,
    StackTranscriptRowUpdateKind UpdateKind,
    string RawPayloadJson,
    bool IsError);

public sealed record StackConfigurationTranscriptRow(
    string Key,
    long Sequence,
    IReadOnlyList<KeyValuePair<string, string>> Entries,
    string RawPayloadJson)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Configuration, Sequence, StackTranscriptRowUpdateKind.Upsert, RawPayloadJson, false);

public sealed record StackResourceTranscriptRow(
    string Key,
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
    bool IsError)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Resource, Sequence, StackTranscriptRowUpdateKind.Upsert, RawPayloadJson, IsError);

public sealed record StackDiagnosticTranscriptRow(
    string Key,
    long Sequence,
    string Severity,
    string? ResourceUrn,
    string Message,
    DiagnosticEvent Event,
    string RawPayloadJson,
    bool IsError)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Diagnostic, Sequence, StackTranscriptRowUpdateKind.Append, RawPayloadJson, IsError);

public sealed record StackTextTranscriptRow(
    string Key,
    long Sequence,
    string Channel,
    string Text,
    StdoutEngineEvent Event,
    string RawPayloadJson,
    bool IsError)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Text, Sequence, StackTranscriptRowUpdateKind.Append, RawPayloadJson, IsError);

public sealed record StackSummaryTranscriptRow(
    string Key,
    long Sequence,
    string Summary,
    SummaryEvent Event,
    string RawPayloadJson)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Summary, Sequence, StackTranscriptRowUpdateKind.Append, RawPayloadJson, false);

public sealed record StackSummaryViewData(
    IReadOnlyList<StackSummaryResourceViewData> Resources,
    IReadOnlyList<string> OutputLines,
    IReadOnlyList<string> ResourceSummaryLines,
    string? Duration,
    bool ShowInfoColumn);

public sealed record StackSummaryResourceViewData(
    string TypeText,
    string Name,
    string Status,
    string Info);

public sealed record StackUnknownTranscriptRow(
    string Key,
    long Sequence,
    string Text,
    string RawPayloadJson)
    : StackTranscriptRow(Key, StackTranscriptRowKind.Unknown, Sequence, StackTranscriptRowUpdateKind.Append, RawPayloadJson, false);
