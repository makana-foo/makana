using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Stacks;

public sealed class IndexModel(PulumiApiClient apiClient, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Org { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Project { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Stack { get; set; } = string.Empty;

    public StackDetails? Details { get; private set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Org) || string.IsNullOrWhiteSpace(Project) || string.IsNullOrWhiteSpace(Stack))
        {
            return RedirectToPage("/Index");
        }

        try
        {
            Details = await apiClient.GetStackAsync(Org, Project, Stack, cancellationToken);
            if (Details is null)
            {
                return NotFound();
            }

            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading stack {Stack} in {Org}/{Project}", Stack, Org, Project);
            return Page();
        }
    }
}
