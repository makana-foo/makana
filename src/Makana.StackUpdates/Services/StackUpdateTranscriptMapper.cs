using Makana.StackUpdates.Models;
using Makana.StackUpdates.Pulumi;

namespace Makana.StackUpdates.Services;

public sealed class StackUpdateTranscriptMapper(UpdateProjectionReducer projectionReducer)
{
    public StackUpdateTranscriptMapper() : this(new UpdateProjectionReducer())
    {
    }

    public IReadOnlyList<StackTranscriptRow> Map(IReadOnlyList<EngineEvent> events)
    {
        var projection = projectionReducer.Replay(events);
        return Map(projection);
    }

    public IReadOnlyList<StackTranscriptRow> Map(UpdateProjection projection)
    {
        var rows = new List<StackTranscriptRow>();

        if (projection.Config is not null)
        {
            rows.Add(new StackConfigurationTranscriptRow(
                "config",
                projection.Config.Sequence,
                projection.Config.Entries,
                projection.Config.RawPayloadJson));
        }

        foreach (var resource in projection.Resources)
        {
            rows.Add(new StackResourceTranscriptRow(
                resource.Key,
                resource.Sequence,
                resource.Marker,
                resource.Operation,
                resource.ResourceUrn,
                resource.ParentUrn,
                resource.ResourceType,
                resource.ResourceName,
                resource.Status,
                resource.Info,
                resource.Metadata,
                resource.RawPayloadJson,
                resource.IsError));
        }

        foreach (var diagnostic in projection.Diagnostics)
        {
            rows.Add(new StackDiagnosticTranscriptRow(
                diagnostic.Key,
                diagnostic.Sequence,
                diagnostic.Severity,
                diagnostic.ResourceUrn,
                diagnostic.Message,
                diagnostic.Event,
                diagnostic.RawPayloadJson,
                diagnostic.IsError));
        }

        foreach (var stdout in projection.Stdout)
        {
            rows.Add(new StackTextTranscriptRow(
                stdout.Key,
                stdout.Sequence,
                stdout.Channel,
                stdout.Text,
                stdout.Event,
                stdout.RawPayloadJson,
                stdout.IsError));
        }

        if (projection.Summary is not null)
        {
            rows.Add(new StackSummaryTranscriptRow(
                projection.Summary.Key,
                projection.Summary.Sequence,
                projection.Summary.Summary,
                projection.Summary.Event,
                projection.Summary.RawPayloadJson));
        }

        return rows;
    }
}
