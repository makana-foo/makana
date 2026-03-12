using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Projects;

public sealed class IndexModel(
    ConsoleContextService consoleContextService,
    PulumiApiClient apiClient,
    ILogger<IndexModel> logger) : PageModel
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

        try
        {
            var orgStacks = await apiClient.GetOrganizationStacksAsync(Owner, cancellationToken);
            Stacks = orgStacks.Where(s => string.Equals(s.ProjectName, Project, StringComparison.Ordinal)).ToArray();
            return Page();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Stacks = [];
            return ConsolePageResults.ConsoleNotFound(this);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading project {Project} in {Owner}", Project, Owner);
            Stacks = [];
            return Page();
        }
    }
}
