namespace Makana.Journaling.Replay;

public enum ResourceDependencyKind
{
    Parent = 0,
    Dependency = 1,
    PropertyDependency = 2,
    DeletedWith = 3,
    ReplaceWith = 4,
}

public sealed class StateDependencyRewriteBuilder
{
    private readonly BaseStateResource original;
    private BaseStateResource current;
    private bool edited;

    public StateDependencyRewriteBuilder(BaseStateResource state)
    {
        original = state;
        current = CloneState(state);
    }

    public StateDependencyRewriteBuilder WithAllUpdatedDependencies(
        Func<string, string> updateProviderReference,
        Func<string, string> updateUrn,
        Func<ResourceDependencyKind, bool>? include = null)
    {
        if (!string.IsNullOrEmpty(current.Provider))
        {
            SetString(current.Provider, updateProviderReference(current.Provider), static (state, value) => state with { Provider = value });
        }

        if (ShouldInclude(ResourceDependencyKind.Parent, include) && !string.IsNullOrEmpty(current.Parent))
        {
            SetString(current.Parent, updateUrn(current.Parent), static (state, value) => state with { Parent = value });
        }

        if (ShouldInclude(ResourceDependencyKind.Dependency, include) && current.Dependencies is { Count: > 0 })
        {
            var updatedDependencies = new List<string>(current.Dependencies.Count);
            var dependencyEdited = false;

            foreach (var dependency in current.Dependencies)
            {
                var updated = updateUrn(dependency);
                updatedDependencies.Add(updated);
                dependencyEdited |= !string.Equals(updated, dependency, StringComparison.Ordinal);
            }

            if (dependencyEdited)
            {
                current = current with { Dependencies = updatedDependencies };
                edited = true;
            }
        }

        if (ShouldInclude(ResourceDependencyKind.PropertyDependency, include) && current.PropertyDependencies is { Count: > 0 })
        {
            var updatedPropertyDependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var propertyDependencyEdited = false;

            foreach (var (key, values) in current.PropertyDependencies)
            {
                var updatedValues = new List<string>(values.Count);
                foreach (var value in values)
                {
                    var updated = updateUrn(value);
                    updatedValues.Add(updated);
                    propertyDependencyEdited |= !string.Equals(updated, value, StringComparison.Ordinal);
                }

                updatedPropertyDependencies[key] = updatedValues;
            }

            if (propertyDependencyEdited)
            {
                current = current with { PropertyDependencies = updatedPropertyDependencies };
                edited = true;
            }
        }

        if (ShouldInclude(ResourceDependencyKind.DeletedWith, include) && !string.IsNullOrEmpty(current.DeletedWith))
        {
            SetString(current.DeletedWith, updateUrn(current.DeletedWith), static (state, value) => state with { DeletedWith = value });
        }

        if (ShouldInclude(ResourceDependencyKind.ReplaceWith, include) && current.ReplaceWith is { Count: > 0 })
        {
            var updatedReplaceWith = new List<string>(current.ReplaceWith.Count);
            var replaceWithEdited = false;

            foreach (var replaceWith in current.ReplaceWith)
            {
                var updated = updateUrn(replaceWith);
                updatedReplaceWith.Add(updated);
                replaceWithEdited |= !string.Equals(updated, replaceWith, StringComparison.Ordinal);
            }

            if (replaceWithEdited)
            {
                current = current with { ReplaceWith = updatedReplaceWith };
                edited = true;
            }
        }

        return this;
    }

    public BaseStateResource Build() => edited ? current : original;

    private void SetString(
        string existing,
        string updated,
        Func<BaseStateResource, string, BaseStateResource> apply)
    {
        if (string.Equals(existing, updated, StringComparison.Ordinal))
        {
            return;
        }

        current = apply(current, updated);
        edited = true;
    }

    private static BaseStateResource CloneState(BaseStateResource state)
    {
        return state with
        {
            Dependencies = state.Dependencies is null ? null : [.. state.Dependencies],
            PropertyDependencies = state.PropertyDependencies?.ToDictionary(
                static entry => entry.Key,
                static entry => (IReadOnlyList<string>)[.. entry.Value],
                StringComparer.Ordinal),
            ReplaceWith = state.ReplaceWith is null ? null : [.. state.ReplaceWith],
        };
    }

    private static bool ShouldInclude(ResourceDependencyKind kind, Func<ResourceDependencyKind, bool>? include)
        => include?.Invoke(kind) ?? true;
}
