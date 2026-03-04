namespace Makana.Journaling.Replay;

public enum JournalEntryKind
{
    Begin = 0,
    Success = 1,
    Failure = 2,
    RefreshSuccess = 3,
    Outputs = 4,
    Write = 5,
    SecretsManager = 6,
    RebuiltBaseState = 7,
}

public sealed record ResourceState(
    string Urn,
    string? Type = null,
    bool Delete = false,
    bool PendingReplacement = false,
    string? Id = null,
    IReadOnlyList<string>? Dependencies = null,
    string? RawJson = null);

public sealed record PendingOperation(
    string Type,
    ResourceState Resource,
    string? RawJson = null);

public sealed record DeploymentSnapshot(
    IReadOnlyList<ResourceState> Resources,
    IReadOnlyList<PendingOperation> PendingOperations,
    string? SecretsProviderRawJson = null);

public sealed record JournalEntry(
    JournalEntryKind Kind,
    long SequenceId,
    long OperationId = 0,
    long? RemoveOld = null,
    long? RemoveNew = null,
    long? PendingReplacementOld = null,
    long? PendingReplacementNew = null,
    long? DeleteOld = null,
    long? DeleteNew = null,
    ResourceState? State = null,
    PendingOperation? Operation = null,
    bool IsRefresh = false,
    DeploymentSnapshot? NewSnapshot = null,
    string? SecretsProviderRawJson = null);
