using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Resources;

public sealed class IndexModel(ConsoleContextService consoleContextService) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    public IReadOnlyList<StackSummary> Stacks { get; private set; } = [];

    public int TotalResources { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        Owner = context.SelectedOwner;
        Stacks = context.SelectedOwnerStacks
            .OrderBy(stack => stack.ProjectName, StringComparer.Ordinal)
            .ThenBy(stack => stack.StackName, StringComparer.Ordinal)
            .ToArray();
        TotalResources = Stacks.Sum(stack => stack.ResourceCount ?? 0);

        return Page();
    }
}
