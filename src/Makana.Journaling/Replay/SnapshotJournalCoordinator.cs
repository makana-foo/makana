using Makana.Journaling.Checkpointing;

namespace Makana.Journaling.Replay;

public sealed record class SnapshotResourceState
{
    public required string Urn { get; init; }

    public string? Type { get; init; }

    public bool Delete { get; init; }

    public bool PendingReplacement { get; init; }

    public string? Id { get; init; }

    public IReadOnlyList<string>? Dependencies { get; init; }

    public IReadOnlyDictionary<string, CheckpointPropertyValue>? Inputs { get; init; }

    public IReadOnlyDictionary<string, CheckpointPropertyValue>? Outputs { get; init; }

    public bool External { get; init; }

    public bool Custom { get; init; }

    public string? Provider { get; init; }

    public DeploymentCustomTimeouts? CustomTimeouts { get; init; }

    public bool RetainOnDelete { get; init; }

    public bool Protect { get; init; }

    public string? Parent { get; init; }

    public string? DeletedWith { get; init; }

    public IReadOnlyList<string>? ReplaceWith { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? PropertyDependencies { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ResourceHooks { get; init; }

    public string? SourcePosition { get; init; }

    public SnapshotResourceState DeepClone()
    {
        return new SnapshotResourceState
        {
            Urn = Urn,
            Type = Type,
            Delete = Delete,
            PendingReplacement = PendingReplacement,
            Id = Id,
            Dependencies = Dependencies is null ? null : [.. Dependencies],
            Inputs = ClonePropertyMap(Inputs),
            Outputs = ClonePropertyMap(Outputs),
            External = External,
            Custom = Custom,
            Provider = Provider,
            CustomTimeouts = CustomTimeouts is null
                ? null
                : new DeploymentCustomTimeouts(
                    CreateSeconds: CustomTimeouts.CreateSeconds,
                    UpdateSeconds: CustomTimeouts.UpdateSeconds,
                    DeleteSeconds: CustomTimeouts.DeleteSeconds),
            RetainOnDelete = RetainOnDelete,
            Protect = Protect,
            Parent = Parent,
            DeletedWith = DeletedWith,
            ReplaceWith = ReplaceWith is null ? null : [.. ReplaceWith],
            PropertyDependencies = CloneStringListMap(PropertyDependencies),
            ResourceHooks = CloneStringListMap(ResourceHooks),
            SourcePosition = SourcePosition,
        };
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>>? CloneStringListMap(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? map)
    {
        if (map is null)
        {
            return null;
        }

        return map.ToDictionary(
            static entry => entry.Key,
            static entry => (IReadOnlyList<string>)[.. entry.Value],
            StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<string, CheckpointPropertyValue>? ClonePropertyMap(
        IReadOnlyDictionary<string, CheckpointPropertyValue>? map)
    {
        if (map is null)
        {
            return null;
        }

        return map.ToDictionary(
            static entry => entry.Key,
            static entry => ClonePropertyValue(entry.Value),
            StringComparer.Ordinal);
    }

    private static CheckpointPropertyValue ClonePropertyValue(CheckpointPropertyValue value)
    {
        return value.Kind switch
        {
            CheckpointPropertyKind.Object => CheckpointPropertyValue.Object(
                value.ObjectValue!.ToDictionary(
                    static entry => entry.Key,
                    static entry => ClonePropertyValue(entry.Value),
                    StringComparer.Ordinal)),
            CheckpointPropertyKind.Array => CheckpointPropertyValue.Array(
            [
                .. value.ArrayValue!.Select(ClonePropertyValue),
            ]),
            _ => value,
        };
    }
}

public sealed record SnapshotPendingOperationState(
    long OperationId,
    string Type,
    SnapshotResourceState Resource)
{
    public SnapshotPendingOperationState DeepClone() => new(OperationId, Type, Resource.DeepClone());
}

public sealed class SnapshotIntegrityErrorMetadata
{
    public required string Message { get; init; }
}

public sealed class SnapshotMetadata
{
    public SnapshotIntegrityErrorMetadata? IntegrityErrorMetadata { get; init; }

    public SnapshotMetadata Clone()
    {
        return new SnapshotMetadata
        {
            IntegrityErrorMetadata = IntegrityErrorMetadata is null
                ? null
                : new SnapshotIntegrityErrorMetadata { Message = IntegrityErrorMetadata.Message },
        };
    }
}

public sealed class SnapshotState
{
    public required IReadOnlyList<SnapshotResourceState> Resources { get; init; }

    public required IReadOnlyList<SnapshotPendingOperationState> PendingOperations { get; init; }

    public SnapshotMetadata Metadata { get; init; } = new();

    public SnapshotState Clone()
    {
        return new SnapshotState
        {
            Resources = [.. Resources.Select(static resource => resource.DeepClone())],
            PendingOperations = [.. PendingOperations.Select(static operation => operation.DeepClone())],
            Metadata = Metadata.Clone(),
        };
    }
}

public enum SnapshotMutationKind
{
    Same = 0,
    Create = 1,
    Update = 2,
    Delete = 3,
    Read = 4,
    CreateReplacement = 5,
    Replace = 6,
}

public sealed class SnapshotJournalCoordinator
{
    private readonly Action<SnapshotState> persistSnapshot;
    private readonly bool skipCheckpoints;
    private readonly bool disableIntegrityChecking;

    private readonly List<SnapshotResourceState> plannedResources = [];
    private readonly List<SnapshotResourceState> baseResources = [];
    private readonly List<SnapshotPendingOperationState> pendingOperations = [];

    private long nextOperationId = 1;
    private bool hasSavedSnapshots;
    private bool dirty;

    public SnapshotJournalCoordinator(
        SnapshotState baseSnapshot,
        Action<SnapshotState> persistSnapshot,
        bool skipCheckpoints = false,
        bool disableIntegrityChecking = false)
    {
        this.persistSnapshot = persistSnapshot;
        this.skipCheckpoints = skipCheckpoints;
        this.disableIntegrityChecking = disableIntegrityChecking;

        foreach (var resource in baseSnapshot.Resources)
        {
            baseResources.Add(resource);
        }

        foreach (var operation in baseSnapshot.PendingOperations)
        {
            pendingOperations.Add(operation);
        }

        Metadata = baseSnapshot.Metadata.Clone();
        dirty = true;
    }

    public SnapshotMetadata Metadata { get; private set; }

    public SnapshotMutation BeginSame(SnapshotResourceState oldState, SnapshotResourceState newState)
        => new(this, SnapshotMutationKind.Same, 0, oldState, newState);

    public SnapshotMutation BeginCreate(SnapshotResourceState newState)
    {
        var operationId = nextOperationId++;
        pendingOperations.Add(new SnapshotPendingOperationState(operationId, "creating", newState));
        PersistCurrent();
        return new SnapshotMutation(this, SnapshotMutationKind.Create, operationId, null, newState);
    }

    public SnapshotMutation BeginUpdate(SnapshotResourceState oldState, SnapshotResourceState newState)
    {
        var operationId = nextOperationId++;
        pendingOperations.Add(new SnapshotPendingOperationState(operationId, "updating", newState));
        PersistCurrent();
        return new SnapshotMutation(this, SnapshotMutationKind.Update, operationId, oldState, newState);
    }

    public SnapshotMutation BeginDelete(SnapshotResourceState oldState)
    {
        var operationId = nextOperationId++;
        pendingOperations.Add(new SnapshotPendingOperationState(operationId, "deleting", oldState));
        PersistCurrent();
        return new SnapshotMutation(this, SnapshotMutationKind.Delete, operationId, oldState, null);
    }

    public SnapshotMutation BeginRead(SnapshotResourceState? oldState, SnapshotResourceState newState)
    {
        var operationId = nextOperationId++;
        pendingOperations.Add(new SnapshotPendingOperationState(operationId, "reading", newState));
        PersistCurrent();
        return new SnapshotMutation(this, SnapshotMutationKind.Read, operationId, oldState, newState);
    }

    public SnapshotMutation BeginCreateReplacement(SnapshotResourceState oldState, SnapshotResourceState newState)
        => new(this, SnapshotMutationKind.CreateReplacement, 0, oldState, newState);

    public SnapshotMutation BeginReplace(SnapshotResourceState oldState, SnapshotResourceState newState)
        => new(this, SnapshotMutationKind.Replace, 0, oldState, newState);

    public void RegisterResourceOutputs(SnapshotResourceState oldState, SnapshotResourceState newState)
    {
        if (PropertyMapEqual(oldState.Outputs, newState.Outputs))
        {
            return;
        }

        ReplaceInCurrentState(oldState, newState);
        PersistCurrent();
    }

    public void Close()
    {
        var validationError = ValidateIntegrity();
        if (validationError is null)
        {
            if (Metadata.IntegrityErrorMetadata is not null)
            {
                Metadata = new SnapshotMetadata();
                dirty = true;
            }
        }
        else
        {
            Metadata = new SnapshotMetadata
            {
                IntegrityErrorMetadata = new SnapshotIntegrityErrorMetadata { Message = validationError },
            };
            dirty = true;
        }

        if (!hasSavedSnapshots || dirty)
        {
            PersistCurrent();
        }

        if (validationError is not null && !disableIntegrityChecking)
        {
            throw new InvalidOperationException($"failed to verify snapshot: {validationError}");
        }
    }

    internal void EndMutation(SnapshotMutation mutation, bool successful)
    {
        switch (mutation.Kind)
        {
            case SnapshotMutationKind.Same:
                EndSame(mutation.OldState!, mutation.NewState!, successful);
                break;
            case SnapshotMutationKind.Create:
                RemovePendingOperation(mutation.OperationId);
                if (successful)
                {
                    plannedResources.Add(mutation.NewState!);
                }

                PersistCurrent();
                break;
            case SnapshotMutationKind.Update:
                RemovePendingOperation(mutation.OperationId);
                if (successful)
                {
                    MoveToPlannedResources(mutation.OldState!, mutation.NewState!);
                }

                PersistCurrent();
                break;
            case SnapshotMutationKind.Delete:
                RemovePendingOperation(mutation.OperationId);
                if (successful)
                {
                    RemoveFromCurrentState(mutation.OldState!);
                }

                PersistCurrent();
                break;
            case SnapshotMutationKind.Read:
                RemovePendingOperation(mutation.OperationId);
                if (successful)
                {
                    if (mutation.OldState is null)
                    {
                        plannedResources.Add(mutation.NewState!);
                    }
                    else
                    {
                        MoveToPlannedResources(mutation.OldState, mutation.NewState!);
                    }
                }

                PersistCurrent();
                break;
            case SnapshotMutationKind.CreateReplacement:
                if (successful)
                {
                    plannedResources.Add(mutation.NewState!);
                    PersistCurrent();
                }

                break;
            case SnapshotMutationKind.Replace:
                if (successful)
                {
                    var original = FindCurrentResourceExact(mutation.OldState!);
                    ReplaceInCurrentStateExact(original, original with { Delete = true });
                    PersistCurrent();
                }

                break;
        }
    }

    private void EndSame(SnapshotResourceState oldState, SnapshotResourceState newState, bool successful)
    {
        if (!successful)
        {
            PersistCurrent();
            return;
        }

        var meaningfulChange = SameStepCheckpointWriteDecider.MustWrite(ToSameStepState(oldState), ToSameStepState(newState));
        var changesProgramOrder = !DependencyListEqual(oldState.Dependencies, newState.Dependencies);

        if (meaningfulChange && changesProgramOrder)
        {
            MoveToPlannedResources(oldState, newState);
        }
        else
        {
            ReplaceInCurrentState(oldState, newState);
        }

        if (skipCheckpoints || !meaningfulChange)
        {
            dirty = true;
            return;
        }

        PersistCurrent();
    }

    private void PersistCurrent()
    {
        persistSnapshot(BuildSnapshot().Clone());
        hasSavedSnapshots = true;
        dirty = false;
    }

    private SnapshotState BuildSnapshot()
    {
        return new SnapshotState
        {
            Resources = [.. plannedResources, .. baseResources],
            PendingOperations = [.. pendingOperations],
            Metadata = Metadata.Clone(),
        };
    }

    private void RemovePendingOperation(long operationId)
    {
        var index = pendingOperations.FindIndex(operation => operation.OperationId == operationId);
        if (index >= 0)
        {
            pendingOperations.RemoveAt(index);
        }
    }

    private void MoveToPlannedResources(SnapshotResourceState oldState, SnapshotResourceState newState)
    {
        RemoveFromCurrentState(oldState);
        plannedResources.Add(newState);
    }

    private void ReplaceInCurrentState(SnapshotResourceState oldState, SnapshotResourceState newState)
    {
        if (TryReplace(plannedResources, oldState, newState))
        {
            return;
        }

        if (TryReplace(baseResources, oldState, newState))
        {
            return;
        }

        throw new InvalidOperationException($"could not locate resource '{oldState.Urn}' in current snapshot state");
    }

    private void RemoveFromCurrentState(SnapshotResourceState oldState)
    {
        if (TryRemove(plannedResources, oldState) || TryRemove(baseResources, oldState))
        {
            return;
        }

        throw new InvalidOperationException($"could not locate resource '{oldState.Urn}' in current snapshot state");
    }

    private SnapshotResourceState FindCurrentResource(SnapshotResourceState oldState)
    {
        var resource = FindResource(plannedResources, oldState) ?? FindResource(baseResources, oldState);
        return resource ?? throw new InvalidOperationException($"could not locate resource '{oldState.Urn}' in current snapshot state");
    }

    private SnapshotResourceState FindCurrentResourceExact(SnapshotResourceState oldState)
    {
        var resource = FindResourceExact(plannedResources, oldState) ?? FindResourceExact(baseResources, oldState);
        return resource ?? throw new InvalidOperationException($"could not locate exact resource '{oldState.Urn}' in current snapshot state");
    }

    private string? ValidateIntegrity()
    {
        var allUrns = BuildSnapshot().Resources.Select(static resource => resource.Urn).ToHashSet(StringComparer.Ordinal);
        foreach (var resource in BuildSnapshot().Resources)
        {
            foreach (var dependency in resource.Dependencies ?? [])
            {
                if (!allUrns.Contains(dependency))
                {
                    return $"missing dependency '{dependency}' for resource '{resource.Urn}'";
                }
            }
        }

        return null;
    }

    private static SameStepResourceState ToSameStepState(SnapshotResourceState resource)
    {
        return new SameStepResourceState
        {
            Urn = resource.Urn,
            Type = resource.Type,
            Delete = resource.Delete,
            External = resource.External,
            Custom = resource.Custom,
            CustomTimeouts = resource.CustomTimeouts,
            RetainOnDelete = resource.RetainOnDelete,
            Id = resource.Id,
            Provider = resource.Provider,
            Parent = resource.Parent,
            DeletedWith = resource.DeletedWith,
            ReplaceWith = resource.ReplaceWith,
            Protect = resource.Protect,
            Inputs = resource.Inputs,
            Outputs = resource.Outputs,
            Dependencies = resource.Dependencies,
            PropertyDependencies = resource.PropertyDependencies,
            ResourceHooks = resource.ResourceHooks,
            SourcePosition = resource.SourcePosition,
        };
    }

    private static bool TryReplace(List<SnapshotResourceState> resources, SnapshotResourceState oldState, SnapshotResourceState newState)
    {
        var index = FindIndex(resources, oldState);
        if (index < 0)
        {
            return false;
        }

        resources[index] = newState;
        return true;
    }

    private void ReplaceInCurrentStateExact(SnapshotResourceState oldState, SnapshotResourceState newState)
    {
        if (TryReplaceExact(plannedResources, oldState, newState))
        {
            return;
        }

        if (TryReplaceExact(baseResources, oldState, newState))
        {
            return;
        }

        throw new InvalidOperationException($"could not locate exact resource '{oldState.Urn}' in current snapshot state");
    }

    private static bool TryReplaceExact(List<SnapshotResourceState> resources, SnapshotResourceState oldState, SnapshotResourceState newState)
    {
        var index = FindExactIndex(resources, oldState);
        if (index < 0)
        {
            return false;
        }

        resources[index] = newState;
        return true;
    }

    private static bool TryRemove(List<SnapshotResourceState> resources, SnapshotResourceState oldState)
    {
        var index = FindIndex(resources, oldState);
        if (index < 0)
        {
            return false;
        }

        resources.RemoveAt(index);
        return true;
    }

    private static SnapshotResourceState? FindResource(List<SnapshotResourceState> resources, SnapshotResourceState target)
    {
        var index = FindIndex(resources, target);
        return index < 0 ? null : resources[index];
    }

    private static SnapshotResourceState? FindResourceExact(List<SnapshotResourceState> resources, SnapshotResourceState target)
    {
        var index = FindExactIndex(resources, target);
        return index < 0 ? null : resources[index];
    }

    private static int FindIndex(List<SnapshotResourceState> resources, SnapshotResourceState target)
    {
        var exactIndex = FindExactIndex(resources, target);
        if (exactIndex >= 0)
        {
            return exactIndex;
        }

        var urnMatches = resources
            .Select((resource, index) => (resource, index))
            .Where(entry => string.Equals(entry.resource.Urn, target.Urn, StringComparison.Ordinal))
            .ToArray();

        return urnMatches.Length == 1 ? urnMatches[0].index : -1;
    }

    private static int FindExactIndex(List<SnapshotResourceState> resources, SnapshotResourceState target)
    {
        for (var i = 0; i < resources.Count; i++)
        {
            if (ReferenceEquals(resources[i], target))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool PropertyMapEqual(
        IReadOnlyDictionary<string, CheckpointPropertyValue>? left,
        IReadOnlyDictionary<string, CheckpointPropertyValue>? right)
        => SameStepCheckpointWriteDecider.MustWrite(
            new SameStepResourceState { Urn = "urn:left", Outputs = left },
            new SameStepResourceState { Urn = "urn:left", Outputs = right }) is false;

    private static bool DependencyListEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if ((left?.Count ?? 0) == 0 && (right?.Count ?? 0) == 0)
        {
            return true;
        }

        if ((left?.Count ?? 0) != (right?.Count ?? 0))
        {
            return false;
        }

        return left!
            .OrderBy(static value => value, StringComparer.Ordinal)
            .SequenceEqual(right!.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }
}

public sealed class SnapshotMutation(
    SnapshotJournalCoordinator coordinator,
    SnapshotMutationKind kind,
    long operationId,
    SnapshotResourceState? oldState,
    SnapshotResourceState? newState)
{
    public SnapshotMutationKind Kind { get; } = kind;

    public long OperationId { get; } = operationId;

    public SnapshotResourceState? OldState { get; } = oldState;

    public SnapshotResourceState? NewState { get; } = newState;

    public void End(bool successful) => coordinator.EndMutation(this, successful);
}
