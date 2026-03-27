using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Organizations;

public sealed class CreateModel(ConsoleContextService consoleContextService) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    [BindProperty]
    public string OrganizationName { get; set; } = string.Empty;

    public ConsoleNavigationContext Context { get; private set; } = ConsoleNavigationContext.Empty;

    public bool CreationAttempted { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (Context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        Owner = Context.SelectedOwner;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        Context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (Context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        Owner = Context.SelectedOwner;
        CreationAttempted = true;
        return Page();
    }
}
