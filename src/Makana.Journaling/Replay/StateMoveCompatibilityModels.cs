using System.Text;

namespace Makana.Journaling.Replay;

public sealed record StateMovePropertyValue(
    string Value,
    bool IsSecret = false);

public sealed record StateMoveResource(
    string Urn,
    string Type,
    string? Provider = null,
    string? Parent = null,
    string? Id = null,
    bool Custom = false,
    IReadOnlyDictionary<string, string>? Inputs = null,
    IReadOnlyDictionary<string, StateMovePropertyValue>? Outputs = null,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? PropertyDependencies = null,
    string? DeletedWith = null);

public sealed record StateMoveOptions(
    bool IncludeParents = false,
    bool DestinationHasSecretManager = true,
    bool SourceLocked = false);

public sealed record StateMoveResult(
    IReadOnlyList<StateMoveResource> SourceResources,
    IReadOnlyList<StateMoveResource> DestinationResources,
    string Stdout);

public static class StateMoveCompatibility
{
    private const string MissingDestinationSecretManagerMessage =
        "destination stack has no secret manager. To move resources either initialize the stack with a secret manager, or run the pulumi state move command from the destination project directory";

    public static StateMoveResult Move(
        string sourceStackReference,
        string destinationStackReference,
        IReadOnlyList<StateMoveResource> sourceResources,
        IReadOnlyList<StateMoveResource> destinationResources,
        IReadOnlyList<string> args,
        StateMoveOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceStackReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationStackReference);
        ArgumentNullException.ThrowIfNull(sourceResources);
        ArgumentNullException.ThrowIfNull(destinationResources);
        ArgumentNullException.ThrowIfNull(args);

        options ??= new StateMoveOptions();

        if (sourceResources.Count == 0)
        {
            throw new InvalidOperationException("source stack has no resources");
        }

        var source = sourceResources.Select(CloneResource).ToList();
        var destination = destinationResources.Select(CloneResource).ToList();
        var sourceByUrn = source.ToDictionary(resource => resource.Urn, StringComparer.Ordinal);
        var warnings = new List<string>();

        var selectedUrns = new List<string>();
        foreach (var arg in args)
        {
            var match = source.FirstOrDefault(resource => string.Equals(resource.Urn, arg, StringComparison.Ordinal));
            if (match is null)
            {
                warnings.Add($"warning: Resource {arg} not found in source stack");
                continue;
            }

            if (IsProvider(match))
            {
                throw new InvalidOperationException("cannot move provider");
            }

            if (IsRootStack(match))
            {
                foreach (var resource in source.Where(static resource => !IsProvider(resource) && !IsRootStack(resource)))
                {
                    AddUnique(selectedUrns, resource.Urn);
                }

                continue;
            }

            AddUnique(selectedUrns, match.Urn);
        }

        if (selectedUrns.Count == 0)
        {
            throw new InvalidOperationException("no resources found to move");
        }

        var moveSet = ExpandMoveSet(source, selectedUrns, options.IncludeParents);
        var movedResources = source
            .Where(resource => moveSet.Contains(resource.Urn))
            .ToList();

        if (movedResources.Any(resource => resource.Outputs?.Values.Any(static output => output.IsSecret) == true) &&
            !options.DestinationHasSecretManager)
        {
            throw new InvalidOperationException(MissingDestinationSecretManagerMessage);
        }

        if (options.SourceLocked)
        {
            throw new InvalidOperationException("None of the resources have been moved.  Please fix the error and try again");
        }

        var remainingResources = source
            .Where(resource => !moveSet.Contains(resource.Urn))
            .Select(resource => RewriteRemainingResource(resource, moveSet))
            .ToList();

        var dependencyWarnings = BuildDependencyWarnings(source, moveSet, sourceStackReference, destinationStackReference);

        var sourceParts = ParseStackReference(sourceStackReference);
        var destinationParts = ParseStackReference(destinationStackReference);
        EnsureDestinationRoot(destination, destinationParts.Stack, destinationParts.Project);

        var rewrittenBySourceUrn = new Dictionary<string, StateMoveResource>(StringComparer.Ordinal);
        var existingProviders = destination
            .Where(IsProvider)
            .ToDictionary(resource => resource.Urn, resource => resource, StringComparer.Ordinal);

        foreach (var resource in movedResources)
        {
            if (IsProvider(resource) || IsRootStack(resource))
            {
                continue;
            }

            EnsureProviderAvailability(resource, source, destination, sourceParts, destinationParts, existingProviders);
            var rewritten = RewriteMovedResource(
                resource,
                sourceParts,
                destinationParts,
                moveSet,
                sourceByUrn,
                rewrittenBySourceUrn,
                existingProviders);

            if (destination.Any(existing => !IsProvider(existing) &&
                                            string.Equals(existing.Urn, rewritten.Urn, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"resource {rewritten.Urn} already exists in destination stack");
            }

            rewrittenBySourceUrn[resource.Urn] = rewritten;
            destination.Add(rewritten);
        }

        destination = OrderDestinationResources(destination);

        var stdout = BuildStdout(
            warnings,
            [.. movedResources.Where(resource => !IsRootStack(resource) && !IsProvider(resource)).Select(static resource => resource.Urn)],
            dependencyWarnings,
            sourceStackReference,
            destinationStackReference);

        return new StateMoveResult(remainingResources, destination, stdout);
    }

    private static IReadOnlySet<string> ExpandMoveSet(
        IReadOnlyList<StateMoveResource> resources,
        IReadOnlyList<string> selectedUrns,
        bool includeParents)
    {
        var byUrn = resources.ToDictionary(resource => resource.Urn, StringComparer.Ordinal);
        var moveSet = new HashSet<string>(selectedUrns, StringComparer.Ordinal);
        var queue = new Queue<string>(selectedUrns);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var child in resources.Where(resource => string.Equals(resource.Parent, current, StringComparison.Ordinal)))
            {
                if (moveSet.Add(child.Urn))
                {
                    queue.Enqueue(child.Urn);
                }
            }

            if (!includeParents)
            {
                continue;
            }

            if (!byUrn.TryGetValue(current, out var currentResource))
            {
                continue;
            }

            var parentUrn = currentResource.Parent;
            while (!string.IsNullOrWhiteSpace(parentUrn) && byUrn.TryGetValue(parentUrn, out var parentResource))
            {
                if (IsProvider(parentResource) || IsRootStack(parentResource))
                {
                    break;
                }

                if (!moveSet.Add(parentUrn))
                {
                    break;
                }

                queue.Enqueue(parentUrn);
                parentUrn = parentResource.Parent;
            }
        }

        return moveSet;
    }

    private static StateMoveResource RewriteRemainingResource(StateMoveResource resource, IReadOnlySet<string> moveSet)
    {
        var dependencies = resource.Dependencies?
            .Where(dependency => !moveSet.Contains(dependency))
            .ToArray();

        IReadOnlyDictionary<string, IReadOnlyList<string>>? propertyDependencies = null;
        if (resource.PropertyDependencies is not null)
        {
            propertyDependencies = resource.PropertyDependencies.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)[.. entry.Value.Where(dependency => !moveSet.Contains(dependency))],
                StringComparer.Ordinal);
        }

        var deletedWith = moveSet.Contains(resource.DeletedWith ?? string.Empty) ? string.Empty : resource.DeletedWith;

        return resource with
        {
            Dependencies = dependencies,
            PropertyDependencies = propertyDependencies,
            DeletedWith = deletedWith,
        };
    }

    private static IReadOnlyList<string> BuildDependencyWarnings(
        IReadOnlyList<StateMoveResource> resources,
        IReadOnlySet<string> moveSet,
        string sourceStackReference,
        string destinationStackReference)
    {
        var warnings = new List<string>();

        var remainingDependencyLines = new List<string>();
        foreach (var resource in resources.Where(resource => !moveSet.Contains(resource.Urn)))
        {
            foreach (var dependency in resource.Dependencies ?? Array.Empty<string>())
            {
                if (moveSet.Contains(dependency))
                {
                    remainingDependencyLines.Add(
                        $"  - {resource.Urn} has a dependency on {dependency}");
                }
            }

            if (!string.IsNullOrWhiteSpace(resource.DeletedWith) && moveSet.Contains(resource.DeletedWith))
            {
                remainingDependencyLines.Add(
                    $"  - {resource.Urn} is marked as deleted with {resource.DeletedWith}");
            }

            foreach (var propertyDependency in resource.PropertyDependencies ?? new Dictionary<string, IReadOnlyList<string>>())
            {
                foreach (var dependency in propertyDependency.Value)
                {
                    if (moveSet.Contains(dependency))
                    {
                        remainingDependencyLines.Add(
                            $"  - {resource.Urn} ({propertyDependency.Key}) has a property dependency on {dependency}");
                    }
                }
            }
        }

        if (remainingDependencyLines.Count > 0)
        {
            warnings.Add(
                $"The following resources remaining in {sourceStackReference} have dependencies on resources moved to {destinationStackReference}:\n\n" +
                string.Join('\n', remainingDependencyLines));
        }

        var movedDependencyLines = new List<string>();
        foreach (var resource in resources.Where(resource => moveSet.Contains(resource.Urn)))
        {
            foreach (var dependency in resource.Dependencies ?? Array.Empty<string>())
            {
                if (!moveSet.Contains(dependency))
                {
                    movedDependencyLines.Add(
                        $"  - {resource.Urn} has a dependency on {dependency}");
                }
            }
        }

        if (movedDependencyLines.Count > 0)
        {
            warnings.Add(
                $"The following resources being moved to {destinationStackReference} have dependencies on resources in {sourceStackReference}:\n\n" +
                string.Join('\n', movedDependencyLines) +
                "\n\nIf you go ahead with moving these dependencies, it will be necessary to create the appropriate inputs and outputs in the program for the stack the resources are moved to.");
        }

        return warnings;
    }

    private static StateMoveResource RewriteMovedResource(
        StateMoveResource resource,
        StackReferenceParts sourceParts,
        StackReferenceParts destinationParts,
        IReadOnlySet<string> moveSet,
        IReadOnlyDictionary<string, StateMoveResource> sourceByUrn,
        IReadOnlyDictionary<string, StateMoveResource> rewrittenBySourceUrn,
        IReadOnlyDictionary<string, StateMoveResource> existingProviders)
    {
        var rewrittenUrn = RewriteUrn(
            resource.Urn,
            destinationParts.Stack,
            destinationParts.Project,
            resource.Parent,
            rewrittenBySourceUrn);

        var rewrittenParent = RewriteParent(resource.Parent, sourceParts, destinationParts, rewrittenBySourceUrn);
        var rewrittenProvider = RewriteProviderReference(resource.Provider, destinationParts, existingProviders);

        var dependencies = (resource.Dependencies ?? Array.Empty<string>())
            .Where(moveSet.Contains)
            .Select(dependency => RewriteUrn(
                dependency,
                destinationParts.Stack,
                destinationParts.Project,
                FindOriginalParent(dependency, sourceByUrn),
                rewrittenBySourceUrn))
            .ToArray();

        IReadOnlyDictionary<string, IReadOnlyList<string>>? propertyDependencies = null;
        if (resource.PropertyDependencies is not null)
        {
            propertyDependencies = resource.PropertyDependencies.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)[.. entry.Value
                    .Where(moveSet.Contains)
                    .Select(dependency => RewriteUrn(
                        dependency,
                        destinationParts.Stack,
                        destinationParts.Project,
                        FindOriginalParent(dependency, sourceByUrn),
                        rewrittenBySourceUrn))],
                StringComparer.Ordinal);
        }

        var deletedWith = !string.IsNullOrWhiteSpace(resource.DeletedWith) && moveSet.Contains(resource.DeletedWith)
            ? RewriteUrn(resource.DeletedWith, destinationParts.Stack, destinationParts.Project, FindOriginalParent(resource.DeletedWith, sourceByUrn), rewrittenBySourceUrn)
            : string.Empty;

        return resource with
        {
            Urn = rewrittenUrn,
            Parent = rewrittenParent,
            Provider = rewrittenProvider,
            Dependencies = dependencies,
            PropertyDependencies = propertyDependencies,
            DeletedWith = deletedWith,
        };
    }

    private static void EnsureProviderAvailability(
        StateMoveResource resource,
        IReadOnlyList<StateMoveResource> source,
        List<StateMoveResource> destination,
        StackReferenceParts sourceParts,
        StackReferenceParts destinationParts,
        Dictionary<string, StateMoveResource> existingProviders)
    {
        if (string.IsNullOrWhiteSpace(resource.Provider))
        {
            return;
        }

        var providerUrn = ParseProviderUrn(resource.Provider);
        var providerResource = source.FirstOrDefault(candidate => string.Equals(candidate.Urn, providerUrn, StringComparison.Ordinal));
        if (providerResource is null)
        {
            return;
        }

        var destinationProviderUrn = RewriteUrn(
            providerResource.Urn,
            destinationParts.Stack,
            destinationParts.Project,
            providerResource.Parent,
            rewrittenBySourceUrn: new Dictionary<string, StateMoveResource>(StringComparer.Ordinal));

        if (existingProviders.TryGetValue(destinationProviderUrn, out var existingProvider))
        {
            if (!ProviderInputsEqual(existingProvider.Inputs, providerResource.Inputs) ||
                !string.Equals(existingProvider.Type, providerResource.Type, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"provider {destinationProviderUrn} already exists in destination stack");
            }

            return;
        }

        var rewrittenProvider = providerResource with
        {
            Urn = destinationProviderUrn,
            Parent = DefaultRootUrn(destinationParts.Stack, destinationParts.Project),
        };

        destination.Add(rewrittenProvider);
        existingProviders[destinationProviderUrn] = rewrittenProvider;
    }

    private static bool ProviderInputsEqual(
        IReadOnlyDictionary<string, string>? left,
        IReadOnlyDictionary<string, string>? right)
    {
        left ??= new Dictionary<string, string>();
        right ??= new Dictionary<string, string>();

        return left.Count == right.Count &&
               left.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                   .SequenceEqual(right.OrderBy(static pair => pair.Key, StringComparer.Ordinal));
    }

    private static string RewriteParent(
        string? parentUrn,
        StackReferenceParts sourceParts,
        StackReferenceParts destinationParts,
        IReadOnlyDictionary<string, StateMoveResource> rewrittenBySourceUrn)
    {
        if (string.IsNullOrWhiteSpace(parentUrn))
        {
            return DefaultRootUrn(destinationParts.Stack, destinationParts.Project);
        }

        if (rewrittenBySourceUrn.TryGetValue(parentUrn, out var rewrittenParent))
        {
            return rewrittenParent.Urn;
        }

        return DefaultRootUrn(destinationParts.Stack, destinationParts.Project);
    }

    private static string RewriteProviderReference(
        string? providerReference,
        StackReferenceParts destinationParts,
        IReadOnlyDictionary<string, StateMoveResource> existingProviders)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            return string.Empty;
        }

        var separator = providerReference.LastIndexOf("::", StringComparison.Ordinal);
        if (separator < 0)
        {
            return providerReference;
        }

        var providerUrn = providerReference[..separator];
        var providerId = providerReference[(separator + 2)..];
        var destinationProviderUrn = RewriteUrn(
            providerUrn,
            destinationParts.Stack,
            destinationParts.Project,
            null,
            rewrittenBySourceUrn: new Dictionary<string, StateMoveResource>(StringComparer.Ordinal));

        if (existingProviders.TryGetValue(destinationProviderUrn, out var provider) && !string.IsNullOrWhiteSpace(provider.Id))
        {
            providerId = provider.Id;
        }

        return $"{destinationProviderUrn}::{providerId}";
    }

    private static string RewriteUrn(
        string urn,
        string destinationStack,
        string destinationProject,
        string? originalParentUrn,
        IReadOnlyDictionary<string, StateMoveResource> rewrittenBySourceUrn)
    {
        var parsed = ParseUrn(urn);
        var baseType = GetBaseQualifiedType(parsed.QualifiedType);

        string qualifiedType;
        if (!string.IsNullOrWhiteSpace(originalParentUrn) && rewrittenBySourceUrn.TryGetValue(originalParentUrn, out var rewrittenParent))
        {
            var parentQualifiedType = ParseUrn(rewrittenParent.Urn).QualifiedType;
            qualifiedType = $"{parentQualifiedType}${baseType}";
        }
        else
        {
            qualifiedType = baseType;
        }

        return $"urn:pulumi:{destinationStack}::{destinationProject}::{qualifiedType}::{parsed.Name}";
    }

    private static string? FindOriginalParent(string urn, IReadOnlyDictionary<string, StateMoveResource> sourceByUrn)
    {
        return sourceByUrn.TryGetValue(urn, out var resource) ? resource.Parent : null;
    }

    private static void EnsureDestinationRoot(List<StateMoveResource> destination, string destinationStack, string destinationProject)
    {
        if (destination.Any(IsRootStack))
        {
            return;
        }

        destination.Add(new StateMoveResource(
            DefaultRootUrn(destinationStack, destinationProject),
            "pulumi:pulumi:Stack"));
    }

    private static List<StateMoveResource> OrderDestinationResources(List<StateMoveResource> destination)
    {
        var ordered = new List<StateMoveResource>(destination.Count);
        ordered.AddRange(destination.Where(IsRootStack));
        ordered.AddRange(destination.Where(IsProvider));
        ordered.AddRange(destination.Where(resource => !IsRootStack(resource) && !IsProvider(resource)));
        return ordered;
    }

    private static string BuildStdout(
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> movedUrns,
        IReadOnlyList<string> dependencyWarnings,
        string sourceStackReference,
        string destinationStackReference)
    {
        var builder = new StringBuilder();
        foreach (var warning in warnings)
        {
            builder.AppendLine(warning);
        }

        builder.AppendLine($"Planning to move the following resources from {sourceStackReference} to {destinationStackReference}:");
        builder.AppendLine();
        foreach (var urn in movedUrns)
        {
            builder.AppendLine($"  - {urn}");
        }

        builder.AppendLine();
        foreach (var dependencyWarning in dependencyWarnings)
        {
            builder.AppendLine(dependencyWarning);
            builder.AppendLine();
        }

        builder.AppendLine($"Successfully moved resources from {sourceStackReference} to {destinationStackReference}");
        return builder.ToString();
    }

    private static string ParseProviderUrn(string providerReference)
    {
        var separator = providerReference.LastIndexOf("::", StringComparison.Ordinal);
        return separator < 0 ? providerReference : providerReference[..separator];
    }

    private static StackReferenceParts ParseStackReference(string stackReference)
    {
        var parts = stackReference.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3)
        {
            throw new InvalidOperationException($"invalid stack reference: {stackReference}");
        }

        return new StackReferenceParts(parts[0], parts[1], parts[2]);
    }

    private static string DefaultRootUrn(string stack, string project)
        => $"urn:pulumi:{stack}::{project}::pulumi:pulumi:Stack::{project}-{stack}";

    private static ParsedUrn ParseUrn(string urn)
    {
        const string prefix = "urn:pulumi:";
        if (!urn.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"invalid urn: {urn}");
        }

        var remainder = urn[prefix.Length..];
        var parts = remainder.Split(["::"], StringSplitOptions.None);
        if (parts.Length < 4)
        {
            throw new InvalidOperationException($"invalid urn: {urn}");
        }

        return new ParsedUrn(parts[0], parts[1], parts[2], parts[3]);
    }

    private static string GetBaseQualifiedType(string qualifiedType)
    {
        var separator = qualifiedType.LastIndexOf('$');
        return separator < 0 ? qualifiedType : qualifiedType[(separator + 1)..];
    }

    private static bool IsProvider(StateMoveResource resource)
        => resource.Type.StartsWith("pulumi:providers:", StringComparison.Ordinal);

    private static bool IsRootStack(StateMoveResource resource)
        => string.Equals(resource.Type, "pulumi:pulumi:Stack", StringComparison.Ordinal);

    private static StateMoveResource CloneResource(StateMoveResource resource)
        => resource with
        {
            Inputs = resource.Inputs is null
                ? null
                : new Dictionary<string, string>(resource.Inputs, StringComparer.Ordinal),
            Outputs = resource.Outputs is null
                ? null
                : resource.Outputs.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            Dependencies = resource.Dependencies?.ToArray(),
            PropertyDependencies = resource.PropertyDependencies?.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)[.. entry.Value],
                StringComparer.Ordinal),
        };

    private static void AddUnique(List<string> values, string value)
    {
        if (!values.Contains(value, StringComparer.Ordinal))
        {
            values.Add(value);
        }
    }

    private readonly record struct StackReferenceParts(string Owner, string Project, string Stack);

    private readonly record struct ParsedUrn(
        string Stack,
        string Project,
        string QualifiedType,
        string Name);
}
