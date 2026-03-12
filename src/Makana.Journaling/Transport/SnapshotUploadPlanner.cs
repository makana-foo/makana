using System.Buffers;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using Makana.Journaling.Checkpointing;

namespace Makana.Journaling.Transport;

public sealed record SnapshotUploadCapabilities(
    bool SupportsDeltaCheckpointUploads,
    int DeltaCheckpointCutoffSizeBytes = 0,
    int MaxDeploymentSchemaVersion = DeploymentSchemaVersions.Current);

public enum SnapshotUploadRequestKind
{
    Checkpoint = 0,
    CheckpointVerbatim = 1,
    CheckpointDelta = 2,
}

public sealed record SnapshotUploadRequest(
    SnapshotUploadRequestKind Kind,
    int SequenceNumber,
    int Version,
    IReadOnlyList<string> Features,
    string? UntypedDeploymentJson = null,
    string? DeploymentDeltaJson = null,
    string? CheckpointHash = null);

public sealed record WireDeploymentResource(
    string Urn,
    bool Custom = false,
    string Type = "",
    bool RefreshBeforeUpdate = false,
    string? ViewOf = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ResourceHooks = null,
    bool Taint = false,
    IReadOnlyList<string>? ReplaceWith = null,
    string? DeadweightPayload = null);

public sealed record WireDeployment(
    IReadOnlyList<WireDeploymentResource> Resources)
{
    public int GetRequiredSchemaVersion()
        => DeploymentSchemaFeatures.GetRequiredVersion(GetRequiredFeatures());

    public IReadOnlyList<string> GetRequiredFeatures()
    {
        var features = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resource in Resources)
        {
            if (resource.RefreshBeforeUpdate)
            {
                features.Add(DeploymentSchemaFeatures.RefreshBeforeUpdate);
            }

            if (!string.IsNullOrEmpty(resource.ViewOf))
            {
                features.Add(DeploymentSchemaFeatures.Views);
            }

            if (resource.ResourceHooks is { Count: > 0 })
            {
                features.Add(DeploymentSchemaFeatures.Hooks);
            }

            if (resource.Taint)
            {
                features.Add(DeploymentSchemaFeatures.Taint);
            }

            if (resource.ReplaceWith is { Count: > 0 })
            {
                features.Add(DeploymentSchemaFeatures.ReplaceWith);
            }
        }

        return features.OrderBy(static feature => feature, StringComparer.Ordinal).ToArray();
    }
}

public sealed record MarshaledDeployment(
    string RawJson,
    IReadOnlyList<int> SpanOffsets,
    IReadOnlyList<string> Spans);

public sealed record DeploymentDelta(
    int SequenceNumber,
    string CheckpointHash,
    string DeploymentDeltaJson);

public sealed class DeploymentDiffState
{
    private MarshaledDeployment? _lastSavedDeployment;
    private int _sequenceNumber = 1;

    public DeploymentDiffState(int minimalDiffSize)
    {
        if (minimalDiffSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimalDiffSize));
        }

        MinimalDiffSize = minimalDiffSize;
    }

    public int MinimalDiffSize { get; }

    public int SequenceNumber => _sequenceNumber;

    public MarshaledDeployment? LastSavedDeployment => _lastSavedDeployment;

    public bool CanDiff()
        => _lastSavedDeployment is not null;

    public bool ShouldDiff(MarshaledDeployment deployment)
    {
        return _lastSavedDeployment is not null &&
               _lastSavedDeployment.RawJson.Length >= MinimalDiffSize &&
               deployment.RawJson.Length >= MinimalDiffSize;
    }

    public MarshaledDeployment MarshalDeployment(
        WireDeployment deployment,
        int version,
        IReadOnlyList<string>? features)
    {
        var spanWriter = new SpanWriter();
        spanWriter.Append("{\"version\":");
        spanWriter.Append(version.ToString());
        if (features is { Count: > 0 })
        {
            spanWriter.Append(",\"features\":");
            AppendStringArray(spanWriter, features);
        }

        spanWriter.Append(",\"deployment\":{\"manifest\":{\"time\":\"0001-01-01T00:00:00Z\",\"magic\":\"\",\"version\":\"\"},\"metadata\":{}");
        if (deployment.Resources.Count > 0)
        {
            spanWriter.Append(",\"resources\":[");
            for (var i = 0; i < deployment.Resources.Count; i++)
            {
                if (i > 0)
                {
                    spanWriter.Append(",");
                }

                spanWriter.NextSpan();
                AppendResource(spanWriter, deployment.Resources[i]);
            }

            spanWriter.NextSpan();
            spanWriter.Append("]");
        }

        spanWriter.Append("}}");
        return spanWriter.ToMarshaledDeployment();
    }

    public static MarshaledDeployment WrapRawDeployment(string rawJson)
    {
        ArgumentNullException.ThrowIfNull(rawJson);

        return new MarshaledDeployment(
            rawJson,
            [0, rawJson.Length],
            [rawJson]);
    }

    public DeploymentDelta Diff(MarshaledDeployment deployment)
    {
        if (_lastSavedDeployment is null)
        {
            throw new InvalidOperationException("Diff() cannot be called before Saved()");
        }

        var segments = ComputeDiffSegments(_lastSavedDeployment.Spans, deployment.Spans);
        var editsJson = SerializeEdits(_lastSavedDeployment.SpanOffsets, deployment.SpanOffsets, deployment.RawJson, segments);
        return new DeploymentDelta(
            _sequenceNumber,
            TextEditApplier.ComputeSha256Hex(deployment.RawJson),
            editsJson);
    }

    public void Saved(MarshaledDeployment deployment)
    {
        _lastSavedDeployment = deployment;
        _sequenceNumber++;
    }

    private static IReadOnlyList<DiffSegment> ComputeDiffSegments(
        IReadOnlyList<string> before,
        IReadOnlyList<string> after)
    {
        var beforeCount = before.Count;
        var afterCount = after.Count;
        var lcs = new int[beforeCount + 1, afterCount + 1];

        for (var beforeIndex = beforeCount - 1; beforeIndex >= 0; beforeIndex--)
        {
            for (var afterIndex = afterCount - 1; afterIndex >= 0; afterIndex--)
            {
                lcs[beforeIndex, afterIndex] = string.Equals(before[beforeIndex], after[afterIndex], StringComparison.Ordinal)
                    ? lcs[beforeIndex + 1, afterIndex + 1] + 1
                    : Math.Max(lcs[beforeIndex + 1, afterIndex], lcs[beforeIndex, afterIndex + 1]);
            }
        }

        var segments = new List<DiffSegment>();
        var currentBefore = 0;
        var currentAfter = 0;
        int? rangeStartBefore = null;
        int? rangeStartAfter = null;

        while (currentBefore < beforeCount || currentAfter < afterCount)
        {
            if (currentBefore < beforeCount &&
                currentAfter < afterCount &&
                string.Equals(before[currentBefore], after[currentAfter], StringComparison.Ordinal))
            {
                FlushPendingSegment();
                currentBefore++;
                currentAfter++;
                continue;
            }

            rangeStartBefore ??= currentBefore;
            rangeStartAfter ??= currentAfter;

            var canDelete = currentBefore < beforeCount;
            var canInsert = currentAfter < afterCount;

            if (canDelete && (!canInsert || lcs[currentBefore + 1, currentAfter] >= lcs[currentBefore, currentAfter + 1]))
            {
                currentBefore++;
                continue;
            }

            if (canInsert)
            {
                currentAfter++;
                continue;
            }

            currentBefore++;
        }

        FlushPendingSegment();
        return segments;

        void FlushPendingSegment()
        {
            if (rangeStartBefore is null || rangeStartAfter is null)
            {
                return;
            }

            segments.Add(new DiffSegment(rangeStartBefore.Value, currentBefore, rangeStartAfter.Value, currentAfter));
            rangeStartBefore = null;
            rangeStartAfter = null;
        }
    }

    private static string SerializeEdits(
        IReadOnlyList<int> beforeOffsets,
        IReadOnlyList<int> afterOffsets,
        string afterRawJson,
        IReadOnlyList<DiffSegment> segments)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(
            buffer,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        writer.WriteStartArray();
        foreach (var segment in segments)
        {
            var startOffset = beforeOffsets[segment.Start];
            var endOffset = beforeOffsets[segment.End];
            var replacementStart = afterOffsets[segment.ReplacementStart];
            var replacementEnd = afterOffsets[segment.ReplacementEnd];

            writer.WriteStartObject();
            writer.WritePropertyName("Span");
            writer.WriteStartObject();
            writer.WriteString("uri", string.Empty);
            writer.WritePropertyName("start");
            writer.WriteStartObject();
            writer.WriteNumber("line", 1);
            writer.WriteNumber("column", 1);
            writer.WriteNumber("offset", startOffset);
            writer.WriteEndObject();
            writer.WritePropertyName("end");
            writer.WriteStartObject();
            writer.WriteNumber("line", 1);
            writer.WriteNumber("column", 1);
            writer.WriteNumber("offset", endOffset);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteString("NewText", afterRawJson.AsSpan(replacementStart, replacementEnd - replacementStart));
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void AppendStringArray(SpanWriter spanWriter, IReadOnlyList<string> values)
    {
        spanWriter.Append("[");
        for (var i = 0; i < values.Count; i++)
        {
            if (i > 0)
            {
                spanWriter.Append(",");
            }

            spanWriter.Append(JsonSerializer.Serialize(values[i]));
        }

        spanWriter.Append("]");
    }

    private static void AppendResource(SpanWriter spanWriter, WireDeploymentResource resource)
    {
        spanWriter.Append("{\"urn\":");
        spanWriter.Append(JsonSerializer.Serialize(resource.Urn));
        spanWriter.Append(",\"custom\":");
        spanWriter.Append(resource.Custom ? "true" : "false");
        spanWriter.Append(",\"type\":");
        spanWriter.Append(JsonSerializer.Serialize(resource.Type));

        if (resource.DeadweightPayload is not null)
        {
            spanWriter.Append(",\"outputs\":{\"deadweight\":");
            spanWriter.Append(JsonSerializer.Serialize(resource.DeadweightPayload));
            spanWriter.Append("}");
        }

        if (resource.RefreshBeforeUpdate)
        {
            spanWriter.Append(",\"refreshBeforeUpdate\":true");
        }

        if (!string.IsNullOrEmpty(resource.ViewOf))
        {
            spanWriter.Append(",\"viewOf\":");
            spanWriter.Append(JsonSerializer.Serialize(resource.ViewOf));
        }

        if (resource.ResourceHooks is { Count: > 0 })
        {
            spanWriter.Append(",\"resourceHooks\":{");
            var firstHook = true;
            foreach (var (hookName, hookUrns) in resource.ResourceHooks.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                if (!firstHook)
                {
                    spanWriter.Append(",");
                }

                firstHook = false;
                spanWriter.Append(JsonSerializer.Serialize(hookName));
                spanWriter.Append(":");
                AppendStringArray(spanWriter, hookUrns);
            }

            spanWriter.Append("}");
        }

        if (resource.Taint)
        {
            spanWriter.Append(",\"taint\":true");
        }

        if (resource.ReplaceWith is { Count: > 0 })
        {
            spanWriter.Append(",\"replaceWith\":");
            AppendStringArray(spanWriter, resource.ReplaceWith);
        }

        spanWriter.Append("}");
    }

    private readonly record struct DiffSegment(
        int Start,
        int End,
        int ReplacementStart,
        int ReplacementEnd);

    private sealed class SpanWriter
    {
        private readonly StringBuilder _builder = new();
        private readonly List<int> _offsets = [];
        private readonly List<string> _spans = [];
        private int _start;

        public void Append(string value)
            => _builder.Append(value);

        public void NextSpan()
        {
            _spans.Add(_builder.ToString(_start, _builder.Length - _start));
            _offsets.Add(_start);
            _start = _builder.Length;
        }

        public MarshaledDeployment ToMarshaledDeployment()
        {
            NextSpan();
            _offsets.Add(_builder.Length);
            return new MarshaledDeployment(_builder.ToString(), _offsets, _spans);
        }
    }
}

public sealed class SnapshotUploadPlanner
{
    private readonly SnapshotUploadCapabilities _capabilities;
    private readonly DeploymentDiffState? _diffState;

    public SnapshotUploadPlanner(SnapshotUploadCapabilities capabilities)
    {
        _capabilities = capabilities;
        if (capabilities.SupportsDeltaCheckpointUploads)
        {
            _diffState = new DeploymentDiffState(capabilities.DeltaCheckpointCutoffSizeBytes);
        }
    }

    public SnapshotUploadRequest PlanSave(WireDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        var version = deployment.GetRequiredSchemaVersion();
        var features = deployment.GetRequiredFeatures();
        if (version > _capabilities.MaxDeploymentSchemaVersion)
        {
            version = _capabilities.MaxDeploymentSchemaVersion;
            features = DeploymentSchemaFeatures.FilterFeaturesForVersion(version, features);
        }

        var marshaledDeployment = (_diffState ?? new DeploymentDiffState(0))
            .MarshalDeployment(deployment, version, features);

        if (_diffState is null)
        {
            return new SnapshotUploadRequest(
                SnapshotUploadRequestKind.Checkpoint,
                SequenceNumber: 0,
                Version: version,
                Features: features,
                UntypedDeploymentJson: marshaledDeployment.RawJson);
        }

        if (!_diffState.ShouldDiff(marshaledDeployment))
        {
            var request = new SnapshotUploadRequest(
                SnapshotUploadRequestKind.CheckpointVerbatim,
                _diffState.SequenceNumber,
                version,
                features,
                UntypedDeploymentJson: marshaledDeployment.RawJson);
            _diffState.Saved(marshaledDeployment);
            return request;
        }

        var delta = _diffState.Diff(marshaledDeployment);
        _diffState.Saved(marshaledDeployment);
        return new SnapshotUploadRequest(
            SnapshotUploadRequestKind.CheckpointDelta,
            delta.SequenceNumber,
            version,
            features,
            DeploymentDeltaJson: delta.DeploymentDeltaJson,
            CheckpointHash: delta.CheckpointHash);
    }
}
