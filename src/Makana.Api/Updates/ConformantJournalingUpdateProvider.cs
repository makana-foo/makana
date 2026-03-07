using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Makana.Api.Contracts;
using Makana.Api.Endpoints;
using Makana.Api.Storage;
using Makana.Journaling.Checkpointing;
using Makana.Journaling.Replay;
using Microsoft.Data.Sqlite;

namespace Makana.Api.Updates;

internal sealed class ConformantJournalingUpdateProvider(
    BackendStore store,
    EndpointHelpers endpointHelpers) : IUpdateCheckpointJournalingProvider
{
    private const int SupportedJournalVersion = 1;

    public int NegotiateJournalVersion(int? clientMaxJournalVersion)
    {
        if (clientMaxJournalVersion is null || clientMaxJournalVersion <= 0)
        {
            return 0;
        }

        return Math.Min(clientMaxJournalVersion.Value, SupportedJournalVersion);
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

    public Task<UpdateMutationResult> ApplyCheckpointVerbatimAsync(
        string updateId,
        PatchUpdateCheckpointVerbatimRequest request,
        CancellationToken cancellationToken)
    {
        var rawDeployment = request.UntypedDeployment.GetRawText();
        var signature = $"verbatim:{request.Version}:{rawDeployment}";
        return ApplySequencedCheckpointAsync(
            updateId,
            request.SequenceNumber,
            request.Version,
            signature,
            baselineDeployment => new CheckpointSession(
                lastSequenceNumber: baselineDeployment.LastSequence,
                currentDeploymentJson: baselineDeployment.DeploymentJson),
            session => session.ApplyVerbatim(new VerbatimCheckpointRequest(
                request.SequenceNumber,
                request.Version,
                rawDeployment)),
            existingCheckpoint =>
                existingCheckpoint.Version == request.Version &&
                string.Equals(existingCheckpoint.DeploymentJson, rawDeployment, StringComparison.Ordinal),
            cancellationToken);
    }

    public Task<UpdateMutationResult> ApplyCheckpointDeltaAsync(
        string updateId,
        PatchUpdateCheckpointDeltaRequest request,
        CancellationToken cancellationToken)
    {
        var deltaJson = request.DeploymentDelta.GetRawText();
        var signature = $"delta:{request.Version}:{request.CheckpointHash}:{deltaJson}";
        return ApplySequencedCheckpointAsync(
            updateId,
            request.SequenceNumber,
            request.Version,
            signature,
            baselineDeployment => new CheckpointSession(
                lastSequenceNumber: baselineDeployment.LastSequence,
                currentDeploymentJson: baselineDeployment.DeploymentJson),
            session => session.ApplyDelta(new DeltaCheckpointRequest(
                request.SequenceNumber,
                request.Version,
                request.CheckpointHash,
                deltaJson)),
            existingCheckpoint =>
                existingCheckpoint.Version == request.Version &&
                string.Equals(ComputeSha256Hex(existingCheckpoint.DeploymentJson), request.CheckpointHash, StringComparison.Ordinal),
            cancellationToken);
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

        // Pulumi may emit "write" entries containing full snapshots. Persist the latest
        // immediately when present to improve recovery if the process crashes mid-update.
        var latestSnapshot = TryGetLatestWriteSnapshot(entries);
        if (latestSnapshot.HasValue)
        {
            await store.SaveCheckpointAsync(
                updateId,
                latestSnapshot.Value.GetRawText(),
                version: 3,
                cancellationToken);
        }
    }

    public async Task<bool> FinalizeSucceededUpdateAsync(string updateId, CancellationToken cancellationToken)
    {
        var updateContext = await store.GetUpdateContextAsync(updateId, cancellationToken);
        if (updateContext is null)
        {
            return false;
        }

        var latestCheckpoint = await store.GetLatestCheckpointForUpdateAsync(updateId, cancellationToken);
        var baselineDeploymentJson = latestCheckpoint?.DeploymentJson;
        if (string.IsNullOrWhiteSpace(baselineDeploymentJson))
        {
            var latestDeployment = await store.GetLatestDeploymentAsync(updateContext.Value.StackId, cancellationToken);
            baselineDeploymentJson = latestDeployment?.DeploymentJson;
        }

        var journalEntriesJson = await store.GetJournalEntriesAsync(updateId, cancellationToken);
        var journalEntries = ParseJournalEntries(journalEntriesJson);
        if (journalEntries.Count == 0)
        {
            return latestCheckpoint is not null;
        }

        var replayEngine = new JournalReplayEngine();
        var baselineSnapshot = ParseDeploymentSnapshot(baselineDeploymentJson);
        var replayed = replayEngine.Replay(baselineSnapshot, journalEntries);
        var materializedDeploymentJson = BuildDeploymentJson(replayed, baselineDeploymentJson);

        await store.SaveCheckpointAsync(
            updateId,
            materializedDeploymentJson,
            updateContext.Value.Version,
            cancellationToken);
        return true;
    }

    private async Task<UpdateMutationResult> ApplySequencedCheckpointAsync(
        string updateId,
        int sequenceNumber,
        int requestVersion,
        string requestSignature,
        Func<BaselineDeployment, CheckpointSession> sessionFactory,
        Func<CheckpointSession, CheckpointApplyResult> applyMutation,
        Func<BackendStore.StoredUpdateCheckpoint, bool> legacyDuplicateMatcher,
        CancellationToken cancellationToken)
    {
        var updateContext = await store.GetUpdateContextAsync(updateId, cancellationToken);
        if (updateContext is null)
        {
            return new UpdateMutationResult(UpdateMutationStatus.NotFound);
        }

        var existingAtSequence = await store.GetCheckpointBySequenceForUpdateAsync(updateId, sequenceNumber, cancellationToken);
        if (existingAtSequence is not null)
        {
            if (IsDuplicate(existingAtSequence.Value, requestSignature, legacyDuplicateMatcher))
            {
                return new UpdateMutationResult(UpdateMutationStatus.Ok);
            }

            return new UpdateMutationResult(
                UpdateMutationStatus.Conflict,
                $"checkpoint sequence {sequenceNumber} was already applied with different payload");
        }

        var latestCheckpoint = await store.GetLatestCheckpointForUpdateAsync(updateId, cancellationToken);
        var expectedSequence = (latestCheckpoint?.Sequence ?? 0) + 1;
        if (sequenceNumber != expectedSequence)
        {
            return new UpdateMutationResult(
                UpdateMutationStatus.BadRequest,
                $"checkpoint sequence out of order: expected {expectedSequence}, got {sequenceNumber}");
        }

        var baseline = await ResolveBaselineDeploymentAsync(updateContext.Value.StackId, latestCheckpoint, cancellationToken);
        var session = sessionFactory(baseline);

        CheckpointApplyResult applyResult;
        try
        {
            applyResult = applyMutation(session);
        }
        catch (CheckpointDeltaBaselineMissingException)
        {
            return new UpdateMutationResult(UpdateMutationStatus.BadRequest, "no baseline deployment available for delta");
        }
        catch (CheckpointHashMismatchException)
        {
            return new UpdateMutationResult(UpdateMutationStatus.Conflict, "checkpoint hash mismatch");
        }
        catch (CheckpointSequenceOutOfOrderException ex)
        {
            return new UpdateMutationResult(UpdateMutationStatus.BadRequest, ex.Message);
        }
        catch (CheckpointSequenceConflictException ex)
        {
            return new UpdateMutationResult(UpdateMutationStatus.Conflict, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new UpdateMutationResult(UpdateMutationStatus.BadRequest, ex.Message);
        }

        try
        {
            var stored = await store.SaveCheckpointWithSequenceAsync(
                updateId,
                applyResult.StoredSequenceNumber,
                applyResult.DeploymentJson,
                requestVersion,
                requestSignature,
                cancellationToken);
            return stored
                ? new UpdateMutationResult(UpdateMutationStatus.Ok)
                : new UpdateMutationResult(UpdateMutationStatus.NotFound);
        }
        catch (SqliteException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Retry-safe idempotency for concurrent duplicate writes.
            var checkpoint = await store.GetCheckpointBySequenceForUpdateAsync(updateId, sequenceNumber, cancellationToken);
            if (checkpoint is not null && IsDuplicate(checkpoint.Value, requestSignature, legacyDuplicateMatcher))
            {
                return new UpdateMutationResult(UpdateMutationStatus.Ok);
            }

            return new UpdateMutationResult(
                UpdateMutationStatus.Conflict,
                $"checkpoint sequence {sequenceNumber} was already applied with different payload");
        }
    }

    private async Task<BaselineDeployment> ResolveBaselineDeploymentAsync(
        string stackId,
        BackendStore.StoredUpdateCheckpoint? latestCheckpoint,
        CancellationToken cancellationToken)
    {
        if (latestCheckpoint is not null)
        {
            return new BaselineDeployment(
                latestCheckpoint.Value.Sequence,
                latestCheckpoint.Value.DeploymentJson);
        }

        var latestDeployment = await store.GetLatestDeploymentAsync(stackId, cancellationToken);
        return new BaselineDeployment(
            LastSequence: 0,
            DeploymentJson: latestDeployment?.DeploymentJson);
    }

    private static bool IsDuplicate(
        BackendStore.StoredUpdateCheckpoint existingCheckpoint,
        string requestSignature,
        Func<BackendStore.StoredUpdateCheckpoint, bool> legacyDuplicateMatcher)
    {
        if (!string.IsNullOrWhiteSpace(existingCheckpoint.RequestSignature))
        {
            return string.Equals(existingCheckpoint.RequestSignature, requestSignature, StringComparison.Ordinal);
        }

        // Legacy rows were created before request signatures existed.
        return legacyDuplicateMatcher(existingCheckpoint);
    }

    private static string ComputeSha256Hex(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static bool IsUniqueConstraintViolation(SqliteException exception)
    {
        return exception.SqliteErrorCode == 19;
    }

    private static JsonElement? TryGetLatestWriteSnapshot(JsonElement entries)
    {
        if (entries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? latestSnapshot = null;
        long latestSequenceId = long.MinValue;
        var hasSequence = false;

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetJournalKind(entry, out var kind) || kind != 5)
            {
                continue;
            }

            if (!entry.TryGetProperty("newSnapshot", out var snapshot) ||
                snapshot.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryGetSequenceId(entry, out var sequenceId))
            {
                if (!hasSequence || sequenceId >= latestSequenceId)
                {
                    hasSequence = true;
                    latestSequenceId = sequenceId;
                    latestSnapshot = snapshot;
                }
            }
            else if (!hasSequence)
            {
                latestSnapshot = snapshot;
            }
        }

        return latestSnapshot;
    }

    private static List<JournalEntry> ParseJournalEntries(IEnumerable<string> events)
    {
        var entries = new List<JournalEntry>();
        foreach (var eventJson in events)
        {
            try
            {
                using var doc = JsonDocument.Parse(eventJson);
                if (TryParseJournalEntry(doc.RootElement, out var entry))
                {
                    entries.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Ignore non-JSON events.
            }
        }

        return entries;
    }

    private static bool TryParseJournalEntry(JsonElement element, out JournalEntry entry)
    {
        entry = default!;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!TryGetJournalKind(element, out var kindInt) ||
            !TryGetSequenceId(element, out var sequenceId) ||
            !Enum.IsDefined(typeof(JournalEntryKind), kindInt))
        {
            return false;
        }

        var kind = (JournalEntryKind)kindInt;
        var operationId = TryGetInt64Property(element, "operationID") ?? 0;
        var removeOld = TryGetInt64Property(element, "removeOld");
        var removeNew = TryGetInt64Property(element, "removeNew");
        var pendingReplacementOld = TryGetInt64Property(element, "pendingReplacementOld");
        var pendingReplacementNew = TryGetInt64Property(element, "pendingReplacementNew");
        var deleteOld = TryGetInt64Property(element, "deleteOld");
        var deleteNew = TryGetInt64Property(element, "deleteNew");
        var isRefresh = TryGetBoolProperty(element, "isRefresh") ?? false;

        ResourceState? state = null;
        if (element.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.Object)
        {
            state = ParseResourceState(stateElement);
        }

        PendingOperation? operation = null;
        if (element.TryGetProperty("operation", out var operationElement) && operationElement.ValueKind == JsonValueKind.Object)
        {
            operation = ParsePendingOperation(operationElement);
        }

        DeploymentSnapshot? newSnapshot = null;
        if (element.TryGetProperty("newSnapshot", out var newSnapshotElement) &&
            newSnapshotElement.ValueKind == JsonValueKind.Object)
        {
            newSnapshot = ParseDeploymentSnapshot(newSnapshotElement);
        }

        var secretsProviderRawJson = element.TryGetProperty("secretsProvider", out var secretsProviderElement) &&
                                     secretsProviderElement.ValueKind == JsonValueKind.Object
            ? secretsProviderElement.GetRawText()
            : null;

        entry = new JournalEntry(
            Kind: kind,
            SequenceId: sequenceId,
            OperationId: operationId,
            RemoveOld: removeOld,
            RemoveNew: removeNew,
            PendingReplacementOld: pendingReplacementOld,
            PendingReplacementNew: pendingReplacementNew,
            DeleteOld: deleteOld,
            DeleteNew: deleteNew,
            State: state,
            Operation: operation,
            IsRefresh: isRefresh,
            NewSnapshot: newSnapshot,
            SecretsProviderRawJson: secretsProviderRawJson);

        return true;
    }

    private static DeploymentSnapshot ParseDeploymentSnapshot(string? deploymentJson)
    {
        if (string.IsNullOrWhiteSpace(deploymentJson))
        {
            return new DeploymentSnapshot(Resources: [], PendingOperations: []);
        }

        try
        {
            using var doc = JsonDocument.Parse(deploymentJson);
            return ParseDeploymentSnapshot(doc.RootElement);
        }
        catch (JsonException)
        {
            return new DeploymentSnapshot(Resources: [], PendingOperations: []);
        }
    }

    private static DeploymentSnapshot ParseDeploymentSnapshot(JsonElement element)
    {
        var resources = new List<ResourceState>();
        if (element.TryGetProperty("resources", out var resourcesElement) &&
            resourcesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var resourceElement in resourcesElement.EnumerateArray())
            {
                if (resourceElement.ValueKind == JsonValueKind.Object)
                {
                    resources.Add(ParseResourceState(resourceElement));
                }
            }
        }

        var operations = new List<PendingOperation>();
        if (TryGetPendingOperations(element, out var operationsElement))
        {
            foreach (var operationElement in operationsElement.EnumerateArray())
            {
                if (operationElement.ValueKind == JsonValueKind.Object)
                {
                    operations.Add(ParsePendingOperation(operationElement));
                }
            }
        }

        var secretsProviderRawJson = TryGetSecretsProviderRawJson(element);
        return new DeploymentSnapshot(resources, operations, secretsProviderRawJson);
    }

    private static ResourceState ParseResourceState(JsonElement element)
    {
        var urn = TryGetStringProperty(element, "urn") ?? string.Empty;
        var type = TryGetStringProperty(element, "type");
        var id = TryGetStringProperty(element, "id");
        var delete = TryGetBoolProperty(element, "delete") ?? false;
        var pendingReplacement = TryGetBoolProperty(element, "pendingReplacement") ?? false;
        var dependencies = ParseStringArrayProperty(element, "dependencies");

        return new ResourceState(
            Urn: urn,
            Type: type,
            Delete: delete,
            PendingReplacement: pendingReplacement,
            Id: id,
            Dependencies: dependencies,
            RawJson: element.GetRawText());
    }

    private static PendingOperation ParsePendingOperation(JsonElement element)
    {
        var type = TryGetStringProperty(element, "type") ?? string.Empty;
        ResourceState resource;
        if (element.TryGetProperty("resource", out var resourceElement) &&
            resourceElement.ValueKind == JsonValueKind.Object)
        {
            resource = ParseResourceState(resourceElement);
        }
        else
        {
            resource = new ResourceState(string.Empty);
        }

        return new PendingOperation(type, resource, element.GetRawText());
    }

    private string BuildDeploymentJson(DeploymentSnapshot snapshot, string? baselineDeploymentJson)
    {
        var template = ParseJsonNode(baselineDeploymentJson) as JsonObject;
        template ??= ParseJsonNode(endpointHelpers.CreateEmptyDeploymentJson()) as JsonObject;
        template ??= new JsonObject();

        var resources = new JsonArray();
        foreach (var resource in snapshot.Resources)
        {
            resources.Add(ParseJsonNode(resource.RawJson) ?? BuildMinimalResourceNode(resource));
        }

        var pendingOperations = new JsonArray();
        foreach (var operation in snapshot.PendingOperations)
        {
            pendingOperations.Add(ParseJsonNode(operation.RawJson) ?? BuildMinimalOperationNode(operation));
        }

        template["resources"] = resources;
        var pendingKey = template.ContainsKey("pendingOperations")
            ? "pendingOperations"
            : "pending_operations";
        template[pendingKey] = pendingOperations;

        if (!string.IsNullOrWhiteSpace(snapshot.SecretsProviderRawJson))
        {
            var secretsProviderNode = ParseJsonNode(snapshot.SecretsProviderRawJson);
            if (secretsProviderNode is not null)
            {
                var secretsProvidersKey = template.ContainsKey("secretsProviders")
                    ? "secretsProviders"
                    : "secrets_providers";
                template[secretsProvidersKey] = secretsProviderNode;
            }
        }

        return template.ToJsonString();
    }

    private static JsonObject BuildMinimalResourceNode(ResourceState resource)
    {
        var node = new JsonObject
        {
            ["urn"] = resource.Urn,
        };

        if (!string.IsNullOrWhiteSpace(resource.Type))
        {
            node["type"] = resource.Type;
        }

        if (!string.IsNullOrWhiteSpace(resource.Id))
        {
            node["id"] = resource.Id;
        }

        if (resource.Delete)
        {
            node["delete"] = true;
        }

        if (resource.PendingReplacement)
        {
            node["pendingReplacement"] = true;
        }

        if (resource.Dependencies is { Count: > 0 })
        {
            var dependencies = new JsonArray();
            foreach (var dependency in resource.Dependencies)
            {
                dependencies.Add((JsonNode?)JsonValue.Create(dependency));
            }

            node["dependencies"] = dependencies;
        }

        return node;
    }

    private static JsonObject BuildMinimalOperationNode(PendingOperation operation)
    {
        return new JsonObject
        {
            ["type"] = operation.Type,
            ["resource"] = BuildMinimalResourceNode(operation.Resource),
        };
    }

    private static JsonNode? ParseJsonNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetJournalKind(JsonElement entry, out int kind)
    {
        kind = -1;
        if (!entry.TryGetProperty("kind", out var kindElement))
        {
            return false;
        }

        if (kindElement.ValueKind == JsonValueKind.Number)
        {
            kind = kindElement.GetInt32();
            return true;
        }

        if (kindElement.ValueKind == JsonValueKind.String)
        {
            return int.TryParse(kindElement.GetString(), out kind);
        }

        return false;
    }

    private static bool TryGetSequenceId(JsonElement entry, out long sequenceId)
    {
        sequenceId = 0;
        if (!entry.TryGetProperty("sequenceID", out var sequenceElement))
        {
            return false;
        }

        if (sequenceElement.ValueKind == JsonValueKind.Number)
        {
            return sequenceElement.TryGetInt64(out sequenceId);
        }

        if (sequenceElement.ValueKind == JsonValueKind.String)
        {
            return long.TryParse(sequenceElement.GetString(), out sequenceId);
        }

        return false;
    }

    private static long? TryGetInt64Property(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.Number && propertyValue.TryGetInt64(out var number))
        {
            return number;
        }

        if (propertyValue.ValueKind == JsonValueKind.String &&
            long.TryParse(propertyValue.GetString(), out number))
        {
            return number;
        }

        return null;
    }

    private static bool? TryGetBoolProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue))
        {
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (propertyValue.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return null;
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) ||
            propertyValue.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return propertyValue.GetString();
    }

    private static IReadOnlyList<string>? ParseStringArrayProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var propertyValue) ||
            propertyValue.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var values = new List<string>();
        foreach (var value in propertyValue.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                var str = value.GetString();
                if (!string.IsNullOrWhiteSpace(str))
                {
                    values.Add(str);
                }
            }
        }

        return values;
    }

    private static bool TryGetPendingOperations(JsonElement element, out JsonElement operations)
    {
        operations = default;
        if (element.TryGetProperty("pendingOperations", out operations) &&
            operations.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (element.TryGetProperty("pending_operations", out operations) &&
            operations.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        return false;
    }

    private static string? TryGetSecretsProviderRawJson(JsonElement element)
    {
        if (element.TryGetProperty("secretsProviders", out var secretsProvidersElement) &&
            secretsProvidersElement.ValueKind == JsonValueKind.Object)
        {
            return secretsProvidersElement.GetRawText();
        }

        if (element.TryGetProperty("secrets_providers", out var secretsProvidersLegacyElement) &&
            secretsProvidersLegacyElement.ValueKind == JsonValueKind.Object)
        {
            return secretsProvidersLegacyElement.GetRawText();
        }

        return null;
    }

    private readonly record struct BaselineDeployment(int LastSequence, string? DeploymentJson);
}
