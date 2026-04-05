using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Organizations;

public sealed class CreateModel(
    ConsoleContextService consoleContextService,
    PulumiApiClient apiClient) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string? Owner { get; set; }

    [BindProperty]
    public string OrganizationName { get; set; } = string.Empty;

    public ConsoleNavigationContext Context { get; private set; } = ConsoleNavigationContext.Empty;

    public bool CreationAttempted { get; private set; }

    public string PreviewSlug => Slugify(OrganizationName);

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
        if (string.IsNullOrWhiteSpace(OrganizationName))
        {
            ModelState.AddModelError(nameof(OrganizationName), "Organization name is required.");
            return Page();
        }

        if (string.IsNullOrWhiteSpace(PreviewSlug))
        {
            ModelState.AddModelError(nameof(OrganizationName), "Organization name is invalid.");
            return Page();
        }

        try
        {
            var created = await apiClient.CreateOrganizationAsync(OrganizationName, cancellationToken);
            if (created is null)
            {
                ModelState.AddModelError(string.Empty, "Organization creation failed.");
                return Page();
            }

            return Redirect(created.RedirectPath);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Conflict)
        {
            ModelState.AddModelError(nameof(OrganizationName), "That organization name is unavailable.");
            return Page();
        }
    }

    private static string Slugify(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        var lastWasDash = false;
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
            {
                builder.Append(ch);
                lastWasDash = false;
                continue;
            }

            if (lastWasDash)
            {
                continue;
            }

            builder.Append('-');
            lastWasDash = true;
        }

        return builder.ToString().Trim('-');
    }
}
