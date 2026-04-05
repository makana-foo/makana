using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Projects;

public sealed class IndexModel(
    ConsoleContextService consoleContextService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Owner { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Project { get; set; } = string.Empty;

    public IReadOnlyList<StackSummary> Stacks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Owner) || string.IsNullOrWhiteSpace(Project))
        {
            return RedirectToPage("/Index");
        }

        var context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        Stacks = [.. context.AccessibleStacks
            .Where(stack =>
                string.Equals(stack.OrgName, Owner, StringComparison.Ordinal) &&
                string.Equals(stack.ProjectName, Project, StringComparison.Ordinal))
            .OrderBy(stack => stack.StackName, StringComparer.Ordinal)];

        return Page();
    }
}
