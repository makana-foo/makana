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
        var decodedOrg = DecodeRouteSegment(org);
        var decodedProject = DecodeRouteSegment(Project);
        var decodedStack = DecodeRouteSegment(Stack);

        Org = decodedOrg;
        Project = decodedProject;
        Stack = decodedStack;
        Owner ??= decodedOrg;

        if (string.IsNullOrWhiteSpace(decodedOrg) || string.IsNullOrWhiteSpace(decodedProject) || string.IsNullOrWhiteSpace(decodedStack))
        {
            return RedirectToPage("/Stacks/Index");
        }

        try
        {
            Details = await apiClient.GetStackAsync(decodedOrg, decodedProject, decodedStack, cancellationToken);
            if (Details is null)
            {
                return NotFound();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading stack {Stack} in {Org}/{Project}", decodedStack, decodedOrg, decodedProject);
        }

        return Page();
    }

    private static string DecodeRouteSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch
        {
            return value;
        }
    }
}
