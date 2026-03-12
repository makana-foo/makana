using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages;

public sealed class IndexModel(ConsoleContextService consoleContextService) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    public ConsoleNavigationContext Context { get; private set; } = ConsoleNavigationContext.Empty;

    public int ProjectCount { get; private set; }

    public int ResourceCount { get; private set; }

    public IReadOnlyList<StackSummary> RecentStacks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (Context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        Owner = Context.SelectedOwner;

        ProjectCount = Context.SelectedOwnerStacks
            .Select(stack => stack.ProjectName)
            .Distinct(StringComparer.Ordinal)
            .Count();
        ResourceCount = Context.SelectedOwnerStacks.Sum(stack => stack.ResourceCount ?? 0);

        RecentStacks = Context.SelectedOwnerStacks
            .OrderByDescending(stack => stack.LastUpdate ?? 0)
            .Take(8)
            .ToArray();

        return Page();
    }
}
