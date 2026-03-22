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
        CancellationToken cancellationToken);

    Task<UpdateMutationResult> ApplyCheckpointVerbatimAsync(
        string updateId,
        PatchUpdateCheckpointVerbatimRequest request,
        CancellationToken cancellationToken);

    Task<UpdateMutationResult> ApplyCheckpointDeltaAsync(
        string updateId,
        PatchUpdateCheckpointDeltaRequest request,
        CancellationToken cancellationToken);

    Task RecordJournalEntriesAsync(
        string updateId,
        JsonElement entries,
        CancellationToken cancellationToken);

    Task<bool> RecoverInterruptedUpdateAsync(
        string updateId,
        CancellationToken cancellationToken);

    Task<bool> FinalizeSucceededUpdateAsync(
        string updateId,
        CancellationToken cancellationToken);
}
