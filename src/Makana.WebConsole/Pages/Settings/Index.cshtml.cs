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

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        Owner = Context.SelectedOwner;
    }
}
