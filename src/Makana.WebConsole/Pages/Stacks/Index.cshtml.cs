using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Stacks;

public sealed class IndexModel(ConsoleContextService consoleContextService) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    public ConsoleNavigationContext Context { get; private set; } = ConsoleNavigationContext.Empty;

    public IReadOnlyList<ProjectGroupSummary> Projects { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        Owner = Context.SelectedOwner;

        Projects = Context.SelectedOwnerStacks
            .GroupBy(stack => stack.ProjectName)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var updatedTimes = group
                    .Select(stack => ToDateTimeOffset(stack.LastUpdate))
                    .Where(update => update.HasValue)
                    .Select(update => update!.Value)
                    .ToArray();

                return new ProjectGroupSummary(
                    group.Key,
                    FirstTag(group, "pulumi:description"),
                    FirstTag(group, "pulumi:runtime"),
                    updatedTimes.Length == 0 ? null : updatedTimes.Max(),
                    group
                        .OrderByDescending(stack => stack.LastUpdate ?? 0)
                        .ThenBy(stack => stack.StackName, StringComparer.Ordinal)
                        .ToArray());
            })
            .ToArray();
    }

    private static string? FirstTag(IGrouping<string, StackSummary> group, string key)
        => group
            .Select(stack => stack.Tags is not null && stack.Tags.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static DateTimeOffset? ToDateTimeOffset(long? unixTimestamp)
    {
        if (!unixTimestamp.HasValue || unixTimestamp.Value <= 0)
        {
            return null;
        }

        return unixTimestamp.Value > 1000000000000
            ? DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp.Value)
            : DateTimeOffset.FromUnixTimeSeconds(unixTimestamp.Value);
    }
}
