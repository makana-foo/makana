using Makana.Api.Contracts;
using Makana.StackUpdates.Models;

namespace Makana.Api.Updates;

internal sealed record RecoveredUpdateState(
    UpdateViewInfo Info,
    bool IsInProgress,
    string? ContinuationToken,
    long? LatestSequence,
    UpdateViewConfigEntry[] Configuration,
    UpdateViewOutputEntry[] Outputs,
    UpdateViewResourceEntry[] Resources,
    UpdateViewSummary? Summary,
    StackTranscriptRow[] TranscriptRows);
