using System.Text.Json;
using Makana.Api.Contracts;

namespace Makana.Api.Updates;

internal enum UpdateMutationStatus
{
    Ok = 0,
    NotFound = 1,
    BadRequest = 2,
    Conflict = 3,
}

internal readonly record struct UpdateMutationResult(
    UpdateMutationStatus Status,
    string? Message = null);

internal interface IUpdateCheckpointJournalingProvider
{
    int NegotiateJournalVersion(int? clientMaxJournalVersion);

    Task<UpdateMutationResult> ApplyCheckpointAsync(
        string updateId,
        PatchUpdateCheckpointRequest request,
        CancellationToken cancellationToken = default);

    Task<UpdateMutationResult> ApplyCheckpointVerbatimAsync(
        string updateId,
        PatchUpdateCheckpointVerbatimRequest request,
        CancellationToken cancellationToken = default);

    Task<UpdateMutationResult> ApplyCheckpointDeltaAsync(
        string updateId,
        PatchUpdateCheckpointDeltaRequest request,
        CancellationToken cancellationToken = default);

    Task RecordJournalEntriesAsync(
        string updateId,
        JsonElement entries,
        CancellationToken cancellationToken = default);

    Task<bool> RecoverInterruptedUpdateAsync(
        string updateId,
        CancellationToken cancellationToken = default);

    Task<bool> FinalizeSucceededUpdateAsync(
        string updateId,
        CancellationToken cancellationToken = default);
}
