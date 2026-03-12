namespace Makana.Journaling.Replay;

public sealed record BaseStateResource
{
    public required string Urn { get; init; }

    public string? Provider { get; init; }

    public bool Delete { get; init; }

    public string? Parent { get; init; }

    public IReadOnlyList<string>? Dependencies { get; init; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>>? PropertyDependencies { get; init; }

    public string? DeletedWith { get; init; }

    public IReadOnlyList<string>? ReplaceWith { get; init; }
}

public sealed record RebuiltBaseState(
    IReadOnlyList<BaseStateResource> Resources,
    IReadOnlyDictionary<string, BaseStateResource> Olds,
    IReadOnlyDictionary<string, IReadOnlyList<BaseStateResource>> AllOlds);

public static class BaseStateRepairer
{
    public static RebuiltBaseState Rebuild(IReadOnlyList<BaseStateResource> previousResources)
    {
        var resources = new List<BaseStateResource>(previousResources.Count);
        var referenceable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var state in previousResources)
        {
            var updated = state;

            if (Count(updated.Dependencies) > 0)
            {
                updated = updated with
                {
                    Dependencies = updated.Dependencies!.Where(referenceable.Contains).ToArray(),
                };
            }

            if (Count(updated.PropertyDependencies) > 0)
            {
                updated = updated with
                {
                    PropertyDependencies = updated.PropertyDependencies!
                        .Select(static entry => new KeyValuePair<string, IReadOnlyList<string>>(
                            entry.Key,
                            entry.Value.ToArray()))
                        .Where(entry => entry.Value.Count > 0)
                        .ToDictionary(
                            static entry => entry.Key,
                            entry => (IReadOnlyList<string>)[.. entry.Value.Where(referenceable.Contains)],
                            StringComparer.Ordinal)
                        .Where(static entry => entry.Value.Count > 0)
                        .ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.Ordinal),
                };
            }

            if (!string.IsNullOrEmpty(updated.DeletedWith) && !referenceable.Contains(updated.DeletedWith))
            {
                updated = updated with { DeletedWith = null };
            }

            if (Count(updated.ReplaceWith) > 0 && updated.ReplaceWith!.Any(replaceWith => !referenceable.Contains(replaceWith)))
            {
                updated = updated with { ReplaceWith = null };
            }

            resources.Add(updated);
            referenceable.Add(updated.Urn);
        }

        var repairedResources = UndangleParents(referenceable, resources);
        var repairedOlds = new Dictionary<string, BaseStateResource>(StringComparer.Ordinal);
        var repairedAllOlds = new Dictionary<string, IReadOnlyList<BaseStateResource>>(StringComparer.Ordinal);

        foreach (var resource in repairedResources)
        {
            if (!repairedAllOlds.TryGetValue(resource.Urn, out var existing))
            {
                existing = [];
            }

            repairedAllOlds[resource.Urn] = [.. existing, resource];

            if (!resource.Delete)
            {
                repairedOlds[resource.Urn] = resource;
            }
        }

        return new RebuiltBaseState(repairedResources, repairedOlds, repairedAllOlds);
    }

    private static IReadOnlyList<BaseStateResource> UndangleParents(
        IReadOnlySet<string> referenceable,
        IReadOnlyList<BaseStateResource> resources)
    {
        var result = new List<BaseStateResource>(resources.Count);
        var availableParents = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var resource in resources)
        {
            var repaired = resource;
            if (!string.IsNullOrEmpty(resource.Parent) && !referenceable.Contains(resource.Parent))
            {
                repaired = resource with
                {
                    Parent = availableParents.GetValueOrDefault(resource.Parent),
                };
            }

            result.Add(repaired);
            availableParents[repaired.Urn] = repaired.Parent;
        }

        return result;
    }

    private static int Count<T>(IReadOnlyCollection<T>? values) => values?.Count ?? 0;
}
