using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Settings;

public sealed class IndexModel(ConsoleContextService consoleContextService) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    public ConsoleNavigationContext Context { get; private set; } = ConsoleNavigationContext.Empty;

    public bool IsPersonalSettings { get; private set; }

    public IReadOnlyList<ServiceThemeOption> AvailableThemes => Context.User?.AvailableThemes ?? [];

    public string? SelectedTheme => Context.User?.Theme;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        Context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (Context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        Owner = Context.SelectedOwner;
        IsPersonalSettings = Context.SelectedOwnerIsPersonal;

        return Page();
    }
}
