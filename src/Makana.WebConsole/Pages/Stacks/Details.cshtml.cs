using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Stacks;

public sealed class DetailsModel(PulumiApiClient apiClient, ILogger<DetailsModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "org")]
    public string Org { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    [BindProperty(SupportsGet = true, Name = "project")]
    public string Project { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true, Name = "stack")]
    public string Stack { get; set; } = string.Empty;

    public StackDetails? Details { get; private set; }

    public async Task<IActionResult> OnGetAsync(string org, CancellationToken cancellationToken)
    {
        Org = org;
        Owner ??= org;
        if (string.IsNullOrWhiteSpace(org) || string.IsNullOrWhiteSpace(Project) || string.IsNullOrWhiteSpace(Stack))
        {
            return RedirectToPage("/Stacks/Index");
        }

        try
        {
            Details = await apiClient.GetStackAsync(org, Project, Stack, cancellationToken);
            if (Details is null)
            {
                return NotFound();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading stack {Stack} in {Org}/{Project}", Stack, org, Project);
        }

        return Page();
    }
}
