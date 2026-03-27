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

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (Context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        Owner = Context.SelectedOwner;

        Projects = Context.SelectedOwnerStacks
            .GroupBy(stack => stack.ProjectName)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var updatedTimes = group
                    .Select(stack => WebConsoleDisplayFormatter.ToDateTimeOffset(stack.LastUpdate))
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

        return Page();
    }

    private static string? FirstTag(IGrouping<string, StackSummary> group, string key)
        => group
            .Select(stack => stack.Tags is not null && stack.Tags.TryGetValue(key, out var value) ? value : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

}
