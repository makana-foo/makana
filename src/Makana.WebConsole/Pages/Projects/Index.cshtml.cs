using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Projects;

public sealed class IndexModel(PulumiApiClient apiClient, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Org { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Project { get; set; } = string.Empty;

    public IReadOnlyList<StackSummary> Stacks { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Org) || string.IsNullOrWhiteSpace(Project))
        {
            return RedirectToPage("/Index");
        }

        try
        {
            var orgStacks = await apiClient.GetOrganizationStacksAsync(Org, cancellationToken);
            Stacks = orgStacks.Where(s => string.Equals(s.ProjectName, Project, StringComparison.Ordinal)).ToArray();
            return Page();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading project {Project} in {Org}", Project, Org);
            Stacks = [];
            return Page();
        }
    }
}
