using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages;

public sealed class IndexModel(PulumiApiClient apiClient, ILogger<IndexModel> logger) : PageModel
{
    public ServiceUser? CurrentUser { get; private set; }
    public IReadOnlyList<StackSummary> Stacks { get; private set; } = [];
    public IReadOnlyList<OrganizationSummary> Organizations { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            CurrentUser = await apiClient.GetUserAsync(cancellationToken);
            Stacks = await apiClient.GetUserStacksAsync(cancellationToken);
            Organizations = Stacks
                .GroupBy(s => s.OrgName)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => new OrganizationSummary(
                    g.Key,
                    g.Select(s => s.ProjectName).Distinct(StringComparer.Ordinal).Count(),
                    g.Count()))
                .ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load dashboard data from API");
            Organizations = [];
            Stacks = [];
        }
    }
}

public sealed record OrganizationSummary(string OrgName, int ProjectCount, int StackCount);
