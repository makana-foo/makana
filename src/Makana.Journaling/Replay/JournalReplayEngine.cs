namespace Makana.Journaling.Replay;

public sealed class JournalReplayEngine
{
    public DeploymentSnapshot Replay(DeploymentSnapshot? baseline, IReadOnlyList<JournalEntry> entries)
    {
        var baseSnapshot = CloneSnapshot(baseline ?? new DeploymentSnapshot(Resources: [], PendingOperations: []));
        var orderedEntries = entries.OrderBy(e => e.SequenceId).ToArray();

        var toRemove = new HashSet<long>();
        var toDeleteInBaseline = new HashSet<long>();
        var toReplaceInBaseline = new Dictionary<long, ResourceState>();
        var markAsDeletion = new HashSet<long>();
        var markAsPendingReplacement = new HashSet<long>();
        var operationIdToResourceIndex = new Dictionary<long, long>();
        var incompleteOps = new Dictionary<long, JournalEntry>();
        var newResources = new List<ResourceState>();
        var index = 0L;

        for (var i = 0; i < orderedEntries.Length; i++)
        {
            var entry = orderedEntries[i];
            switch (entry.Kind)
            {
                case JournalEntryKind.Begin:
                    if (entry.OperationId != 0)
                    {
                        incompleteOps[entry.OperationId] = entry;
                    }
                    break;
                case JournalEntryKind.Success:
                    incompleteOps.Remove(entry.OperationId);
                    if (entry.State is not null)
                    {
                        newResources.Add(CloneResource(entry.State));
                        if (entry.OperationId != 0)
                        {
                            operationIdToResourceIndex[entry.OperationId] = index;
                        }

                        index++;
                    }

                    if (entry.RemoveOld is not null)
                    {
                        toDeleteInBaseline.Add(entry.RemoveOld.Value);
                    }

                    if (entry.RemoveNew is not null)
                    {
                        toRemove.Add(entry.RemoveNew.Value);
                    }

                    if (entry.DeleteOld is not null)
                    {
                        markAsDeletion.Add(entry.DeleteOld.Value);
                    }

                    if (entry.DeleteNew is not null &&
                        operationIdToResourceIndex.TryGetValue(entry.DeleteNew.Value, out var deleteNewIndex))
                    {
                        var existing = newResources[(int)deleteNewIndex];
                        newResources[(int)deleteNewIndex] = existing with { Delete = true };
                    }

                    if (entry.PendingReplacementOld is not null)
                    {
                        markAsPendingReplacement.Add(entry.PendingReplacementOld.Value);
                    }

                    if (entry.PendingReplacementNew is not null &&
                        operationIdToResourceIndex.TryGetValue(entry.PendingReplacementNew.Value, out var pendingNewIndex))
                    {
                        var existing = newResources[(int)pendingNewIndex];
                        newResources[(int)pendingNewIndex] = existing with { PendingReplacement = true };
                    }

                    break;
                case JournalEntryKind.RefreshSuccess:
                    incompleteOps.Remove(entry.OperationId);
                    if (entry.RemoveOld is not null)
                    {
                        if (entry.State is null)
                        {
                            toDeleteInBaseline.Add(entry.RemoveOld.Value);
                        }
                        else
                        {
                            toReplaceInBaseline[entry.RemoveOld.Value] = CloneResource(entry.State);
                        }
                    }

                    if (entry.RemoveNew is not null)
                    {
                        if (entry.State is null)
                        {
                            toRemove.Add(entry.RemoveNew.Value);
                        }
                        else if (operationIdToResourceIndex.TryGetValue(entry.RemoveNew.Value, out var replaceNewIndex))
                        {
                            newResources[(int)replaceNewIndex] = CloneResource(entry.State);
                        }
                    }

                    break;
                case JournalEntryKind.Failure:
                    incompleteOps.Remove(entry.OperationId);
                    break;
                case JournalEntryKind.Outputs:
                    if (entry.State is not null && entry.RemoveOld is not null)
                    {
                        toReplaceInBaseline[entry.RemoveOld.Value] = CloneResource(entry.State);
                    }

                    if (entry.State is not null &&
                        entry.RemoveNew is not null &&
                        operationIdToResourceIndex.TryGetValue(entry.RemoveNew.Value, out var outputsIndex))
                    {
                        newResources[(int)outputsIndex] = CloneResource(entry.State);
                    }

                    break;
                case JournalEntryKind.Write:
                    if (entry.NewSnapshot is not null)
                    {
                        baseSnapshot = CloneSnapshot(entry.NewSnapshot);
                    }

                    break;
                case JournalEntryKind.SecretsManager:
                    if (!string.IsNullOrWhiteSpace(entry.SecretsProviderRawJson))
                    {
                        baseSnapshot = baseSnapshot with
                        {
                            SecretsProviderRawJson = entry.SecretsProviderRawJson
                        };
                    }
                    break;
                case JournalEntryKind.RebuiltBaseState:
                    if (newResources.Count > 0 && i < orderedEntries.Length - 1)
                    {
                        throw new InvalidOperationException(
                            $"invalid RebuiltBaseState journal entry: {newResources.Count} new resources exist before final entry");
                    }

                    baseSnapshot = BuildSnapshot(
                        baseSnapshot,
                        newResources,
                        toRemove,
                        toDeleteInBaseline,
                        toReplaceInBaseline,
                        markAsDeletion,
                        markAsPendingReplacement,
                        operationIdToResourceIndex,
                        incompleteOps);

                    toRemove.Clear();
                    toDeleteInBaseline.Clear();
                    toReplaceInBaseline.Clear();
                    markAsDeletion.Clear();
                    markAsPendingReplacement.Clear();
                    operationIdToResourceIndex.Clear();
                    incompleteOps.Clear();
                    newResources.Clear();
                    index = 0;
                    break;
            }
        }

        return BuildSnapshot(
            baseSnapshot,
            newResources,
            toRemove,
            toDeleteInBaseline,
            toReplaceInBaseline,
            markAsDeletion,
            markAsPendingReplacement,
            operationIdToResourceIndex,
            incompleteOps);
    }

    private static DeploymentSnapshot BuildSnapshot(
        DeploymentSnapshot baseSnapshot,
        IReadOnlyList<ResourceState> newResources,
        IReadOnlySet<long> toRemove,
        IReadOnlySet<long> toDeleteInBaseline,
        IReadOnlyDictionary<long, ResourceState> toReplaceInBaseline,
        IReadOnlySet<long> markAsDeletion,
        IReadOnlySet<long> markAsPendingReplacement,
        IReadOnlyDictionary<long, long> operationIdToResourceIndex,
        IReadOnlyDictionary<long, JournalEntry> incompleteOps)
    {
        var removeIndices = new HashSet<long>();
        foreach (var opId in toRemove)
        {
            if (operationIdToResourceIndex.TryGetValue(opId, out var index))
            {
                removeIndices.Add(index);
            }
        }

        var resources = new List<ResourceState>();
        for (var i = 0; i < newResources.Count; i++)
        {
            if (!removeIndices.Contains(i))
            {
                resources.Add(CloneResource(newResources[i]));
            }
        }

        for (var i = 0; i < baseSnapshot.Resources.Count; i++)
        {
            if (toDeleteInBaseline.Contains(i))
            {
                continue;
            }

            if (toReplaceInBaseline.TryGetValue(i, out var replacement))
            {
                resources.Add(CloneResource(replacement));
                continue;
            }

            var resource = CloneResource(baseSnapshot.Resources[i]);
            if (markAsDeletion.Contains(i))
            {
                resource = resource with { Delete = true };
            }

            if (markAsPendingReplacement.Contains(i))
            {
                resource = resource with { PendingReplacement = true };
            }

            resources.Add(resource);
        }

        var operations = new List<PendingOperation>();
        foreach (var entry in incompleteOps.Values)
        {
            if (entry.Operation is not null)
            {
                operations.Add(CloneOperation(entry.Operation));
            }
        }

        foreach (var operation in baseSnapshot.PendingOperations)
        {
            if (string.Equals(operation.Type, "creating", StringComparison.Ordinal))
            {
                operations.Add(CloneOperation(operation));
            }
        }

        return new DeploymentSnapshot(resources, operations, baseSnapshot.SecretsProviderRawJson);
    }

    private static DeploymentSnapshot CloneSnapshot(DeploymentSnapshot snapshot)
    {
        var resources = snapshot.Resources.Select(CloneResource).ToArray();
        var operations = snapshot.PendingOperations.Select(CloneOperation).ToArray();
        return new DeploymentSnapshot(resources, operations, snapshot.SecretsProviderRawJson);
    }

    private static ResourceState CloneResource(ResourceState resource)
    {
        return resource with { Dependencies = resource.Dependencies?.ToArray() };
    }

    private static PendingOperation CloneOperation(PendingOperation operation)
    {
        return operation with { Resource = CloneResource(operation.Resource) };
    }
}
