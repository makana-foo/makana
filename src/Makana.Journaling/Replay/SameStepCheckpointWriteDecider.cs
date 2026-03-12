using Makana.Journaling.Checkpointing;

namespace Makana.Journaling.Replay;

public sealed record SameStepResourceState
{
    public required string Urn { get; init; }

    public string? Type { get; init; }

    public bool Delete { get; init; }

    public bool External { get; init; }

    public bool Custom { get; init; }

    public DeploymentCustomTimeouts? CustomTimeouts { get; init; }

    public bool RetainOnDelete { get; init; }

    public string? Id { get; init; }

    public string? Provider { get; init; }

    public string? Parent { get; init; }

    public string? DeletedWith { get; init; }

    public IReadOnlyList<string>? ReplaceWith { get; init; }

    public bool Protect { get; init; }

    public IReadOnlyDictionary<string, CheckpointPropertyValue>? Inputs { get; init; }

    public IReadOnlyDictionary<string, CheckpointPropertyValue>? Outputs { get; init; }

    public IReadOnlyList<string>? Dependencies { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? PropertyDependencies { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? ResourceHooks { get; init; }

    public IReadOnlyList<string>? InitErrors { get; init; }

    public string? SourcePosition { get; init; }
}

public static class SameStepCheckpointWriteDecider
{
    public static bool MustWrite(SameStepResourceState oldState, SameStepResourceState newState)
    {
        if (oldState.Delete != newState.Delete)
        {
            throw new InvalidOperationException("either both or neither resource must be pending deletion");
        }

        if (oldState.External != newState.External)
        {
            throw new InvalidOperationException("either both or neither resource must be external");
        }

        if (!string.Equals(oldState.Id, newState.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("old and new resource IDs must be equal");
        }

        if (!string.Equals(oldState.Urn, newState.Urn, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(oldState.Type, newState.Type, StringComparison.Ordinal))
        {
            return true;
        }

        if (oldState.Custom != newState.Custom)
        {
            return true;
        }

        if (oldState.CustomTimeouts != newState.CustomTimeouts)
        {
            return true;
        }

        if (oldState.RetainOnDelete != newState.RetainOnDelete)
        {
            return true;
        }

        if (!string.Equals(oldState.Provider, newState.Provider, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(oldState.Parent, newState.Parent, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(oldState.DeletedWith, newState.DeletedWith, StringComparison.Ordinal))
        {
            return true;
        }

        if (!StringListEqual(oldState.ReplaceWith, newState.ReplaceWith))
        {
            return true;
        }

        if (oldState.Protect != newState.Protect)
        {
            return true;
        }

        if (!PropertyMapEqual(oldState.Inputs, newState.Inputs))
        {
            return true;
        }

        if (!PropertyMapEqual(oldState.Outputs, newState.Outputs))
        {
            return true;
        }

        if (!DependencyListEqual(oldState.Dependencies, newState.Dependencies))
        {
            return true;
        }

        if (!DependencyMapEqual(oldState.PropertyDependencies, newState.PropertyDependencies))
        {
            return true;
        }

        if (!StringListMapEqual(oldState.ResourceHooks, newState.ResourceHooks))
        {
            return true;
        }

        return false;
    }

    private static bool PropertyMapEqual(
        IReadOnlyDictionary<string, CheckpointPropertyValue>? left,
        IReadOnlyDictionary<string, CheckpointPropertyValue>? right)
    {
        if (Count(left) != Count(right))
        {
            return false;
        }

        if (Count(left) == 0)
        {
            return true;
        }

        foreach (var (key, leftValue) in left!)
        {
            if (!right!.TryGetValue(key, out var rightValue))
            {
                return false;
            }

            if (!PropertyValueEqual(leftValue, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PropertyValueEqual(CheckpointPropertyValue left, CheckpointPropertyValue right)
    {
        if (left.Kind != right.Kind)
        {
            return false;
        }

        return left.Kind switch
        {
            CheckpointPropertyKind.Null => true,
            CheckpointPropertyKind.String => string.Equals(left.StringValue, right.StringValue, StringComparison.Ordinal),
            CheckpointPropertyKind.Number => left.NumberValue == right.NumberValue,
            CheckpointPropertyKind.Boolean => left.BooleanValue == right.BooleanValue,
            CheckpointPropertyKind.Object => PropertyMapEqual(left.ObjectValue, right.ObjectValue),
            CheckpointPropertyKind.Array => PropertyListEqual(left.ArrayValue, right.ArrayValue),
            _ => false,
        };
    }

    private static bool PropertyListEqual(
        IReadOnlyList<CheckpointPropertyValue>? left,
        IReadOnlyList<CheckpointPropertyValue>? right)
    {
        if (Count(left) != Count(right))
        {
            return false;
        }

        for (var i = 0; i < Count(left); i++)
        {
            if (!PropertyValueEqual(left![i], right![i]))
            {
                return false;
            }
        }

        return true;
    }

    // Pulumi sorts top-level dependency lists before comparing them, so ordering alone is not meaningful.
    private static bool DependencyListEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (Count(left) == 0 && Count(right) == 0)
        {
            return true;
        }

        if (Count(left) != Count(right))
        {
            return false;
        }

        return left!
            .OrderBy(static value => value, StringComparer.Ordinal)
            .SequenceEqual(right!.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private static bool DependencyMapEqual(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? left,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? right)
    {
        if (Count(left) != Count(right))
        {
            return false;
        }

        if (Count(left) == 0)
        {
            return true;
        }

        foreach (var (key, leftValue) in left!)
        {
            if (!right!.TryGetValue(key, out var rightValue))
            {
                return false;
            }

            if ((Count(leftValue) > 0 || Count(rightValue) > 0) && !StringListEqual(leftValue, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringListMapEqual(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? left,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? right)
    {
        if (Count(left) != Count(right))
        {
            return false;
        }

        if (Count(left) == 0)
        {
            return true;
        }

        foreach (var (key, leftValue) in left!)
        {
            if (!right!.TryGetValue(key, out var rightValue))
            {
                return false;
            }

            if (!StringListEqual(leftValue, rightValue))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StringListEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (Count(left) != Count(right))
        {
            return false;
        }

        for (var i = 0; i < Count(left); i++)
        {
            if (!string.Equals(left![i], right![i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static int Count<T>(IReadOnlyCollection<T>? values) => values?.Count ?? 0;
}
