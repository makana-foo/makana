using System.Collections.Frozen;

namespace Makana.WebConsole.Models;

public sealed record ConsoleOwnerOption(string Value, string Label, bool IsPersonal);

public sealed record ConsoleOwnerGroup(string Heading, IReadOnlyList<ConsoleOwnerOption> Options);

public sealed record ConsoleNavigationContext(
    ServiceUser? User,
    string SelectedOwner,
    IReadOnlyList<ConsoleOwnerOption> OwnerOptions,
    IReadOnlyList<ConsoleOwnerGroup> OwnerGroups,
    IReadOnlyList<StackSummary> AccessibleStacks,
    IReadOnlyList<StackSummary> SelectedOwnerStacks)
{
    public static ConsoleNavigationContext Empty { get; } = new(
        null,
        string.Empty,
        [],
        [],
        [],
        []);

    public FrozenSet<string> OwnerSet { get; } = OwnerOptions
        .Select(option => option.Value)
        .ToFrozenSet(StringComparer.Ordinal);
}

public sealed record ProjectGroupSummary(
    string ProjectName,
    string? Description,
    string? Language,
    DateTimeOffset? LastUpdated,
    IReadOnlyList<StackSummary> Stacks);
