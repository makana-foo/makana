using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Makana.WebConsole.Pages.Stacks;

public sealed class DetailsModel(
    PulumiApiClient apiClient,
    StackUpdateTranscriptMapper transcriptMapper,
    IConfiguration configuration,
    ILogger<DetailsModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "owner")]
    public string Owner { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true, Name = "project")]
    public string Project { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true, Name = "stack")]
    public string Stack { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true, Name = "updateId")]
    public string? UpdateId { get; set; }

    public StackDetails? Details { get; private set; }
    public StackLivePanelViewModel LivePanel { get; private set; } = StackLivePanelViewModel.Empty(string.Empty, string.Empty, string.Empty);
    public string StackUpdateHubUrl { get; private set; } = string.Empty;
    public bool EnableLiveRefresh => LivePanel.IsInProgress;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!InitializeRouteContext())
        {
            return RedirectToPage("/Stacks/Index");
        }

        try
        {
            Details = await apiClient.GetStackAsync(Owner, Project, Stack, cancellationToken);
            if (Details is null)
            {
                return NotFound();
            }

            LivePanel = await BuildLivePanelAsync(cancellationToken);
            StackUpdateHubUrl = new Uri(new Uri(PulumiApiClient.GetBrowserApiBaseUrl(configuration), UriKind.Absolute), "/hubs/stack-updates").ToString();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading stack {Stack} in {Owner}/{Project}", Stack, Owner, Project);
        }

        return Page();
    }

    public async Task<IActionResult> OnGetLiveUpdatePanelAsync(CancellationToken cancellationToken)
    {
        if (!InitializeRouteContext())
        {
            return BadRequest();
        }

        try
        {
            Details = await apiClient.GetStackAsync(Owner, Project, Stack, cancellationToken);
            if (Details is null)
            {
                return NotFound();
            }

            LivePanel = await BuildLivePanelAsync(cancellationToken);
            return Partial("_LiveUpdatePanel", LivePanel);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading live update panel for stack {Stack} in {Owner}/{Project}", Stack, Owner, Project);
            return Partial("_LiveUpdatePanel", StackLivePanelViewModel.Empty(Owner, Project, Stack));
        }
    }

    private bool InitializeRouteContext()
    {
        var decodedOwner = DecodeRouteSegment(Owner);
        var decodedProject = DecodeRouteSegment(Project);
        var decodedStack = DecodeRouteSegment(Stack);

        Owner = decodedOwner;
        Project = decodedProject;
        Stack = decodedStack;

        return !string.IsNullOrWhiteSpace(decodedOwner) &&
               !string.IsNullOrWhiteSpace(decodedProject) &&
               !string.IsNullOrWhiteSpace(decodedStack);
    }

    private async Task<StackLivePanelViewModel> BuildLivePanelAsync(CancellationToken cancellationToken)
    {
        var latestInfo = await apiClient.GetLatestUpdateAsync(Owner, Project, Stack, cancellationToken);
        var updateHistory = await apiClient.GetStackUpdatesAsync(Owner, Project, Stack, cancellationToken);
        var selectedUpdateId = string.IsNullOrWhiteSpace(UpdateId)
            ? (!string.IsNullOrWhiteSpace(Details?.ActiveUpdate) ? Details.ActiveUpdate : latestInfo?.Info?.UpdateId)
            : UpdateId;
        if (string.IsNullOrWhiteSpace(selectedUpdateId))
        {
            return new StackLivePanelViewModel(
                Owner,
                Project,
                Stack,
                null,
                false,
                "No active update",
                latestInfo?.Info,
                [],
                updateHistory?.Updates ?? []);
        }

        var updateResults = await apiClient.GetUpdateResultsAsync(Owner, Project, Stack, selectedUpdateId, cancellationToken);
        var eventsResponse = await apiClient.GetUpdateEventsAsync(Owner, Project, Stack, selectedUpdateId, cancellationToken);
        var statusText = updateResults?.Status ?? latestInfo?.Info?.Result ?? "in-progress";
        var isInProgress =
            string.Equals(statusText, "in-progress", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(statusText, "running", StringComparison.OrdinalIgnoreCase);

        var transcriptRows = transcriptMapper.Map(eventsResponse?.Events ?? []);

        return new StackLivePanelViewModel(
            Owner,
            Project,
            Stack,
            selectedUpdateId,
            isInProgress,
            statusText,
            latestInfo?.Info,
            transcriptRows,
            updateHistory?.Updates ?? []);
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
