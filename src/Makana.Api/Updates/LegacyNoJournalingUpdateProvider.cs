using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Makana.Api.Contracts;
using Makana.Api.Endpoints;
using Makana.Api.Storage;

namespace Makana.Api.Updates;

internal sealed class LegacyNoJournalingUpdateProvider(
    UpdateCommandRepository updateCommands,
    UpdateQueryRepository updateQueries,
    UpdateLogRepository updateLog,
    EndpointHelpers endpointHelpers) : IUpdateCheckpointJournalingProvider
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
        var schemaVersion = DeploymentSchemaPolicy.ValidateSupportedVersion(request.Version);
        updateLog.SaveCheckpoint(
            updateId,
            request.Deployment.GetRawText(),
            request.Version,
            schemaVersion);
        return new UpdateMutationResult(UpdateMutationStatus.Ok);
    }

    public async Task<UpdateMutationResult> ApplyCheckpointVerbatimAsync(
        string updateId,
        PatchUpdateCheckpointVerbatimRequest request,
        CancellationToken cancellationToken)
    {
        var schemaVersion = DeploymentSchemaPolicy.ValidateSupportedVersion(request.Version);
        updateLog.SaveCheckpoint(
            updateId,
            GetCheckpointDeploymentJson(request.UntypedDeployment),
            request.Version,
            schemaVersion);
        return new UpdateMutationResult(UpdateMutationStatus.Ok);
    }

    public async Task<UpdateMutationResult> ApplyCheckpointDeltaAsync(
        string updateId,
        PatchUpdateCheckpointDeltaRequest request,
        CancellationToken cancellationToken)
    {
        var schemaVersion = DeploymentSchemaPolicy.ValidateSupportedVersion(request.Version);
        var updateContext = updateCommands.GetUpdateContext(updateId);
        if (updateContext is null)
        {
            return new UpdateMutationResult(UpdateMutationStatus.NotFound);
        }

        var updateCheckpoint = updateLog.GetLatestCheckpointForUpdate(updateId);
        var baselineDeployment = updateCheckpoint?.DeploymentJson;
        if (baselineDeployment is null)
        {
            var latestDeployment = updateQueries.GetLatestDeployment(updateContext.Value.StackId);
            baselineDeployment = latestDeployment?.DeploymentJson;
        }

        if (baselineDeployment is null)
        {
            return new UpdateMutationResult(UpdateMutationStatus.BadRequest, "no baseline deployment available for delta");
        }

        if (!endpointHelpers.TryApplyTextEdits(baselineDeployment, request.DeploymentDelta, out var updatedDeployment))
        {
            return new UpdateMutationResult(UpdateMutationStatus.BadRequest, "invalid deployment delta");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(updatedDeployment))).ToLowerInvariant();
        if (!string.Equals(hash, request.CheckpointHash, StringComparison.Ordinal))
        {
            return new UpdateMutationResult(UpdateMutationStatus.Conflict, "checkpoint hash mismatch");
        }

        updateLog.SaveCheckpoint(updateId, updatedDeployment, request.Version, schemaVersion);
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

        updateLog.RecordJournalEntries(updateId, entriesJson);
    }

    public Task<bool> RecoverInterruptedUpdateAsync(string updateId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    public Task<bool> FinalizeSucceededUpdateAsync(string updateId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(false);
    }

    private static string GetCheckpointDeploymentJson(JsonElement untypedDeployment)
    {
        if (untypedDeployment.ValueKind == JsonValueKind.Object &&
            untypedDeployment.TryGetProperty("deployment", out var deploymentElement) &&
            deploymentElement.ValueKind == JsonValueKind.Object)
        {
            return deploymentElement.GetRawText();
        }

        return untypedDeployment.GetRawText();
    }
}
