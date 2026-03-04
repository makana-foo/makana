using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Endpoints;
using Makana.Api.Storage;

namespace Makana.Api.Updates;

internal sealed class LegacyNoJournalingUpdateProvider(BackendStore store) : IUpdateCheckpointJournalingProvider
{
    public int NegotiateJournalVersion(int? clientMaxJournalVersion)
    {
        _ = clientMaxJournalVersion;
        return 0;
    }

    public async Task<UpdateMutationResult> ApplyCheckpointAsync(
        string updateId,
        PatchUpdateCheckpointRequest request,
        CancellationToken cancellationToken)
    {
        await store.SaveCheckpointAsync(
            updateId,
            request.Deployment.GetRawText(),
            request.Version,
            cancellationToken);
        return new UpdateMutationResult(UpdateMutationStatus.Ok);
    }

    public async Task<UpdateMutationResult> ApplyCheckpointVerbatimAsync(
        string updateId,
        PatchUpdateCheckpointVerbatimRequest request,
        CancellationToken cancellationToken)
    {
        await store.SaveCheckpointAsync(
            updateId,
            request.UntypedDeployment.GetRawText(),
            request.Version,
            cancellationToken);
        return new UpdateMutationResult(UpdateMutationStatus.Ok);
    }

    public async Task<UpdateMutationResult> ApplyCheckpointDeltaAsync(
        string updateId,
        PatchUpdateCheckpointDeltaRequest request,
        CancellationToken cancellationToken)
    {
        var updateContext = await store.GetUpdateContextAsync(updateId, cancellationToken);
        if (updateContext is null)
        {
            return new UpdateMutationResult(UpdateMutationStatus.NotFound);
        }

        var updateCheckpoint = await store.GetLatestCheckpointForUpdateAsync(updateId, cancellationToken);
        var baselineDeployment = updateCheckpoint?.DeploymentJson;
        if (baselineDeployment is null)
        {
            var latestDeployment = await store.GetLatestDeploymentAsync(updateContext.Value.StackId, cancellationToken);
            baselineDeployment = latestDeployment?.DeploymentJson;
        }

        if (baselineDeployment is null)
        {
            return new UpdateMutationResult(UpdateMutationStatus.BadRequest, "no baseline deployment available for delta");
        }

        if (!EndpointHelpers.TryApplyTextEdits(baselineDeployment, request.DeploymentDelta, out var updatedDeployment))
        {
            return new UpdateMutationResult(UpdateMutationStatus.BadRequest, "invalid deployment delta");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(updatedDeployment))).ToLowerInvariant();
        if (!string.Equals(hash, request.CheckpointHash, StringComparison.Ordinal))
        {
            return new UpdateMutationResult(UpdateMutationStatus.Conflict, "checkpoint hash mismatch");
        }

        await store.SaveCheckpointAsync(updateId, updatedDeployment, request.Version, cancellationToken);
        return new UpdateMutationResult(UpdateMutationStatus.Ok);
    }

    public async Task RecordJournalEntriesAsync(
        string updateId,
        JsonElement entries,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entriesJson = new List<string>();
        if (entries.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in entries.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                entriesJson.Add(entry.GetRawText());
            }
        }
        else if (entries.ValueKind != JsonValueKind.Undefined)
        {
            entriesJson.Add(entries.GetRawText());
        }

        await store.RecordJournalEntriesAsync(updateId, entriesJson, cancellationToken);
    }

    public Task<bool> FinalizeSucceededUpdateAsync(string updateId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }
}
