using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Makana.WebConsole.Pages.Stacks;

public sealed class DetailsModel(
    PulumiApiClient apiClient,
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

    [BindProperty(SupportsGet = true, Name = "runType")]
    public string? RunType { get; set; }

    [BindProperty(SupportsGet = true, Name = "updateRef")]
    public string? UpdateReference { get; set; }

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
            LivePanel = await BuildLivePanelAsync(cancellationToken);
            return CreatePartialResult("_LiveUpdatePanel", LivePanel);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading live update panel for stack {Stack} in {Owner}/{Project}", Stack, Owner, Project);
            return CreatePartialResult("_LiveUpdatePanel", StackLivePanelViewModel.Empty(Owner, Project, Stack));
        }
    }

    public async Task<IActionResult> OnGetLiveUpdateDeltaAsync(string? continuationToken, CancellationToken cancellationToken)
    {
        if (!InitializeRouteContext())
        {
            return BadRequest();
        }

        try
        {
            LivePanel = await BuildLivePanelAsync(cancellationToken, continuationToken);
            if (LivePanel.ShowUpdateHistory)
            {
                return CreatePartialResult("_LiveUpdateDelta", LivePanel);
            }

            var terminalRefresh = !string.IsNullOrWhiteSpace(continuationToken) &&
                                  string.IsNullOrWhiteSpace(LivePanel.ContinuationToken) &&
                                  !LivePanel.IsInProgress;
            if (terminalRefresh)
            {
                LivePanel = LivePanel.WithForcedTerminalRefresh();
            }

            if (LivePanel.UpdateView?.Delta is not { } delta)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            if (!delta.InfoChanged &&
                !delta.ChangesChanged &&
                !delta.ConfigurationChanged &&
                !delta.OutputsChanged &&
                !delta.ResourcesChanged &&
                !delta.SummaryChanged)
            {
                return StatusCode(StatusCodes.Status204NoContent);
            }

            return CreatePartialResult("_LiveUpdateDelta", LivePanel);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading live update delta for stack {Stack} in {Owner}/{Project}", Stack, Owner, Project);
            return StatusCode(StatusCodes.Status204NoContent);
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
               !string.IsNullOrWhiteSpace(decodedStack) &&
               IsSupportedRunType(RunType);
    }

    private async Task<StackLivePanelViewModel> BuildLivePanelAsync(CancellationToken cancellationToken, string? continuationToken = null)
    {
        var showStackOverview = string.IsNullOrWhiteSpace(RunType) && string.IsNullOrWhiteSpace(UpdateReference);
        if (showStackOverview)
        {
            var overview = await apiClient.GetStackOverviewAsync(Owner, Project, Stack, cancellationToken);
            var latest = overview?.Latest;
            var latestHistory = overview?.History.FirstOrDefault();
            var latestUpdateId = latestHistory?.UpdateId ?? overview?.Stack.ActiveUpdate;
            var overviewStatusText = latest?.Status ?? "No updates";
            var overviewIsInProgress =
                string.Equals(overviewStatusText, "in-progress", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(overviewStatusText, "running", StringComparison.OrdinalIgnoreCase);

            return new StackLivePanelViewModel(
                Owner,
                Project,
                Stack,
                latestUpdateId,
                continuationToken,
                null,
                null,
                overviewIsInProgress,
                overviewStatusText,
                overview,
                null);
        }

        var selectedUpdate = await ResolveRequestedUpdateAsync(cancellationToken);
        var selectedRunType = selectedUpdate is null
            ? RunType
            : selectedUpdate.RouteRunType;
        var selectedRouteReference = selectedUpdate is null
            ? UpdateReference
            : selectedUpdate.RouteReference;

        var selectedUpdateId = selectedUpdate?.UpdateId;
        if (string.IsNullOrWhiteSpace(selectedUpdateId))
        {
            return new StackLivePanelViewModel(
                Owner,
                Project,
                Stack,
                null,
                null,
                null,
                null,
                false,
                "No active update",
                null,
                null);
        }

        var updateView = await apiClient.GetUpdateViewAsync(Owner, Project, Stack, selectedUpdateId, continuationToken, cancellationToken);
        var statusText = updateView?.Header.Status
            ?? updateView?.Info.Status
            ?? "in-progress";
        var isInProgress =
            string.Equals(statusText, "in-progress", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(statusText, "running", StringComparison.OrdinalIgnoreCase);

        return new StackLivePanelViewModel(
            Owner,
            Project,
            Stack,
            selectedUpdateId,
            continuationToken,
            selectedRunType,
            selectedRouteReference,
            isInProgress,
            statusText,
            null,
            updateView);
    }

    private async Task<ResolvedUpdateReference?> ResolveRequestedUpdateAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(UpdateId))
        {
            return new ResolvedUpdateReference(
                UpdateId,
                null,
                null,
                RunType ?? "updates",
                UpdateReference ?? UpdateId);
        }

        if (string.IsNullOrWhiteSpace(UpdateReference))
        {
            return null;
        }

        if (!string.Equals(RunType, "previews", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(UpdateReference, out var version))
        {
            var resolved = await apiClient.GetUpdateReferenceByVersionAsync(Owner, Project, Stack, version, cancellationToken);
            return resolved is null
                ? null
                : new ResolvedUpdateReference(
                    resolved.UpdateId,
                    resolved.Version,
                    resolved.Kind,
                    resolved.RouteRunType,
                    resolved.RouteReference);
        }

        return new ResolvedUpdateReference(
            UpdateReference,
            null,
            NormalizeKindFromRunType(RunType),
            RunType ?? "previews",
            UpdateReference);
    }

    private static string? NormalizeKindFromRunType(string? runType)
        => (runType ?? string.Empty).ToLowerInvariant() switch
        {
            "previews" => "preview",
            "refreshes" => "refresh",
            "destroys" => "destroy",
            "updates" => "update",
            _ => null
        };

    private static bool IsSupportedRunType(string? runType)
        => string.IsNullOrWhiteSpace(runType) ||
           runType is "previews" or "updates" or "refreshes" or "destroys";

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

    private PartialViewResult CreatePartialResult(string viewName, StackLivePanelViewModel model)
        => new()
        {
            ViewName = viewName,
            ViewData = new ViewDataDictionary<StackLivePanelViewModel>(ViewData, model)
        };

    private sealed record ResolvedUpdateReference(
        string UpdateId,
        int? Version,
        string? Kind,
        string RouteRunType,
        string RouteReference);

}
