using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Organizations;

public sealed class IndexModel(
    ConsoleContextService consoleContextService,
    PulumiApiClient apiClient,
    ILogger<IndexModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Owner { get; set; } = string.Empty;

    public IReadOnlyList<ProjectSummary> Projects { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Owner))
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
            var stacks = await apiClient.GetOrganizationStacksAsync(Owner, cancellationToken);
            Projects = stacks
                .GroupBy(s => s.ProjectName)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new ProjectSummary(g.Key, g.Count()))
                .ToArray();
            return Page();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            Projects = [];
            return ConsolePageResults.ConsoleNotFound(this);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading organization {Owner}", Owner);
            Projects = [];
            return Page();
        }
    }
}

public sealed record ProjectSummary(string ProjectName, int StackCount);
