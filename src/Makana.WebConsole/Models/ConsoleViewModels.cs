using System.Collections.Frozen;

namespace Makana.WebConsole.Models;

public sealed record ConsoleOwnerOption(
    string Value,
    string Label,
    bool IsPersonal,
    bool IsSignedIn,
    bool RequiresOrganizationSignIn);

public sealed record ConsoleOwnerGroup(string Heading, IReadOnlyList<ConsoleOwnerOption> Options);

public sealed record ConsoleNavigationContext(
    ServiceUser? User,
    string SelectedOwner,
    string? RequestedOwner,
    bool RequestedOwnerIsAuthorized,
    IReadOnlyList<ConsoleOwnerOption> OwnerOptions,
    IReadOnlyList<ConsoleOwnerGroup> OwnerGroups,
    IReadOnlyList<StackSummary> AccessibleStacks,
    IReadOnlyList<StackSummary> SelectedOwnerStacks)
{
    public static ConsoleNavigationContext Empty { get; } = new(
        null,
        string.Empty,
        null,
        true,
        [],
        [],
        [],
        []);

    public ConsoleOwnerOption? PersonalOwnerOption => OwnerOptions.FirstOrDefault(option => option.IsPersonal);

    public string PersonalOwner => PersonalOwnerOption?.Value ?? SelectedOwner;

    public bool SelectedOwnerIsPersonal => PersonalOwnerOption is { Value: var personalOwner }
        && string.Equals(personalOwner, SelectedOwner, StringComparison.Ordinal);

    public bool HasRequestedOwner => !string.IsNullOrWhiteSpace(RequestedOwner);

    public bool HasAuthorizedRequestedOwner => HasRequestedOwner && RequestedOwnerIsAuthorized;

    public bool ShowOwnerRouteNotFound => HasRequestedOwner && !RequestedOwnerIsAuthorized;

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

public sealed record ConsolePageHeaderViewModel(
    string Title,
    string? Subtitle = null,
    string? BackHref = null,
    string? BackLabel = null);

public sealed record ConsoleEmptyStateViewModel(
    string Message,
    string? Title = null);

public sealed record ConsoleDetailItem(
    string Label,
    string Value);
