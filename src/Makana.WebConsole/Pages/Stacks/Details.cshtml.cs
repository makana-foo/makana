using Makana.WebConsole.Configuration;
using Makana.WebConsole.Models;
using Makana.WebConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Makana.WebConsole.Pages.Stacks;

public sealed class DetailsModel(
    ConsoleContextService consoleContextService,
    PulumiApiClient apiClient,
    IOptions<WebConsoleApiOptions> apiOptions,
    ILogger<DetailsModel> logger) : PageModel
{
    private readonly WebConsoleApiOptions _apiOptions = apiOptions.Value;

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
    public string StackUpdateViewUrl { get; private set; } = string.Empty;
    public bool EnableLiveRefresh =>
        !LivePanel.ShowUpdateHistory &&
        LivePanel.IsInProgress &&
        !string.IsNullOrWhiteSpace(StackUpdateHubUrl) &&
        !string.IsNullOrWhiteSpace(StackUpdateViewUrl);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!InitializeRouteContext())
        {
            return RedirectToPage("/Stacks/Index");
        }

        var context = await consoleContextService.GetContextAsync(HttpContext, cancellationToken);
        if (context.ShowOwnerRouteNotFound)
        {
            return ConsolePageResults.OwnerRouteNotFound(this);
        }

        try
        {
            LivePanel = await BuildLivePanelAsync(cancellationToken);
            if (!LivePanel.ShowUpdateHistory && LivePanel.IsInProgress)
            {
                StackUpdateViewUrl = BuildApiUri(BuildLiveUpdateViewPath());
                StackUpdateHubUrl = BuildApiUri("/hubs/stack-updates");
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ConsolePageResults.ConsoleNotFound(this);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed loading stack {Stack} in {Owner}/{Project}", Stack, Owner, Project);
        }

        return Page();
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

    public async Task<PartialViewResult> OnGetStackMetadataCardAsync(CancellationToken cancellationToken)
    {
        if (!InitializeRouteContext())
        {
            return Partial("_StackPageMetadataCard", StackLivePanelViewModel.Empty(string.Empty, string.Empty, string.Empty));
        }

        try
        {
            var livePanel = await BuildLivePanelAsync(cancellationToken);
            return Partial("_StackPageMetadataCard", livePanel);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed refreshing stack metadata card for {Owner}/{Project}/{Stack}", Owner, Project, Stack);
            return Partial("_StackPageMetadataCard", StackLivePanelViewModel.Empty(Owner, Project, Stack));
        }
    }

    private string BuildLiveUpdateViewPath()
    {
        if (string.IsNullOrWhiteSpace(LivePanel.UpdateId))
        {
            return string.Empty;
        }

        var runType = LivePanel.SelectedRunType ?? "updates";
        return $"/api/stacks/{Uri.EscapeDataString(Owner)}/{Uri.EscapeDataString(Project)}/{Uri.EscapeDataString(Stack)}/{Uri.EscapeDataString(runType)}/{Uri.EscapeDataString(LivePanel.UpdateId)}/live";
    }

    private string BuildApiUri(string path)
    {
        var baseUrl = ResolveBrowserApiBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return new Uri(new Uri(baseUrl, UriKind.Absolute), path).ToString();
    }

    private string ResolveBrowserApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_apiOptions.BrowserBaseUrl))
        {
            return NormalizePublicUrl(_apiOptions.BrowserBaseUrl);
        }

        if (!string.IsNullOrWhiteSpace(_apiOptions.PublicApiUrl))
        {
            return NormalizePublicUrl(_apiOptions.PublicApiUrl);
        }

        return string.Empty;
    }

    private static string NormalizePublicUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value ?? string.Empty;
        }

        if ((uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443) ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.Port == 80))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        return uri.PathAndQuery == "/" && string.IsNullOrEmpty(uri.Fragment)
            ? uri.GetLeftPart(UriPartial.Authority)
            : uri.GetComponents(UriComponents.AbsoluteUri, UriFormat.UriEscaped);
    }

    private sealed record ResolvedUpdateReference(
        string UpdateId,
        int? Version,
        string? Kind,
        string RouteRunType,
        string RouteReference);

}
