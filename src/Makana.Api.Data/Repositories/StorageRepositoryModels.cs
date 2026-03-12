using Makana.Api.Contracts;

namespace Makana.Api.Storage;

public enum UpdateStartStatus
{
    Started,
    NotFound,
    Conflict,
    InvalidState
}

public readonly record struct StartUpdateResult(UpdateStartStatus Status, int Version, string? Token);
public readonly record struct StoredUpdateCheckpoint(int Sequence, int Version, string DeploymentJson, string? RequestSignature);
public readonly record struct StoredEngineEventPage(IReadOnlyList<string> EventsJson, long? LastSequence);
public readonly record struct ExpiredUpdateCandidate(string UpdateId, string StackId, string Owner, string Project, string Stack);
public readonly record struct ExpiredUpdateCancellationResult(bool Canceled, long? LatestEventSequence);
public readonly record struct StackStorageLocation(string StackId, string ShardPath);
public readonly record struct UpdatePersistenceContext(string StackId, string Kind, int Version, string? ProgramRequestJson);

internal readonly record struct StackCatalogEntry(
    string StackId,
    string OrgId,
    string ProjectId,
    string? ShardPath,
    string? ShardState)
{
    public bool HasActiveShard => !string.IsNullOrWhiteSpace(ShardPath) &&
        string.Equals(ShardState, StorageRepositoryCommon.ActiveShardState, StringComparison.Ordinal);
}

internal sealed record StackCatalogRow(string Id, string OrgId, string ProjectId, string? ShardPath, string? ShardState);
internal sealed record ApiTokenOwnerRow(string UserId, string? OrgId);
internal sealed record UserRow(string Id, string Login, string Name, string? Email, string AvatarUrl);
internal sealed record UserOrgRow(string Id, string Login);
internal sealed record StackIdentityRow(string StackId, string ProjectId, string OrgId);
internal sealed record StackDataRow(string? TagsJson, string? ConfigJson);
internal sealed record StackDeleteRow(string StackId, string? ShardPath);
internal sealed record DeploymentDocumentRow(string Id, string DeploymentJson);
internal sealed record ActiveUpdateMetadataRow(string ActiveUpdate, int Version);
internal sealed class UpdateVersionKindRow
{
    public long Version { get; set; }

    public string Kind { get; set; } = "";

    public string? ProgramRequestJson { get; set; }
}

internal sealed record UpdateLookupRow(string StackId, string Kind, string Status, string? ProgramRequestJson);
internal sealed record StoredUpdateCheckpointRow(int Sequence, int Version, string DeploymentJson, string? RequestSignature);
internal sealed record EngineEventRow(long EngineSequence, string EventJson);
internal sealed record UpdateStatusVersionRow(string Status, int Version);
internal sealed record UpdateDetailsRow(string Id, string StackId, string Kind, string Status, int Version, string CreatedAt, string? CompletedAt, string? ProgramRequestJson);
internal sealed record UpdateContextRow(string StackId, string Kind, int Version, string? ProgramRequestJson);
internal sealed record DeploymentEnvelopeRow(int Version, int SchemaVersion, string DeploymentJson);
