using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Makana.WebConsole.Models;

public sealed record StackUpdateHistoryEntryViewData(
    string Title,
    string KindLabel,
    string RunLabel,
    string StatusText,
    string StartedRelativeText,
    string StartedExactText,
    string? Message,
    string? Href,
    bool IsSelected);

public sealed record StackLivePanelViewModel(
    string Org,
    string Project,
    string Stack,
    string? UpdateId,
    string? RequestedContinuationToken,
    string? SelectedRunType,
    string? SelectedRouteReference,
    bool IsInProgress,
    string StatusText,
    StackOverviewResponse? Overview,
    UpdateViewResponse? UpdateView)
{
    public StackPageMetadata? StackMetadata => Overview?.Stack ?? UpdateView?.Stack;

    public string? ContinuationToken => UpdateView?.ContinuationToken;

    public UpdateViewDelta? Delta => UpdateView?.Delta;

    public bool IsPreview =>
        Overview?.Latest?.IsPreview == true ||
        UpdateView?.Header.IsPreview == true ||
        UpdateView?.Summary?.IsPreview == true;

    public string SummaryStateLabel => IsPreview ? "Plan" : "Status";

    public bool HasChanges => UpdateView?.Changes.HasEvents == true;

    public bool IsIncrementalDelta => Delta?.IsIncremental == true;

    public IReadOnlyList<StackChangeFeedEntryViewModel> ChangeFeedEntries =>
        MapChangeFeedEntries(UpdateView?.Changes.Entries);

    public IReadOnlyList<StackChangeFeedEntryViewModel> DeltaChangeFeedEntries =>
        MapChangeFeedEntries(UpdateView?.Changes.DeltaEntries);

    public IReadOnlyList<UpdateViewConfigEntry> DisplayConfiguration =>
        UpdateView?.Configuration ?? [];

    public IReadOnlyList<UpdateViewOutputEntry> DisplayOutputs =>
        UpdateView?.Outputs ?? [];

    public IReadOnlyList<UpdateViewConsoleLine> SummaryConsoleLines =>
        UpdateView?.Changes.SummaryLines ?? [];

    public IReadOnlyList<UpdateViewConsoleLine> DiffConsoleLines =>
        UpdateView?.Changes.DiffLines ?? [];

    public IReadOnlyList<UpdateViewConsoleLine> DiagnosticConsoleLines =>
        UpdateView?.Changes.DiagnosticLines ?? [];

    public IReadOnlyList<UpdateViewConsoleLineDelta> SummaryLineDeltas => UpdateView?.Changes.SummaryLineDeltas ?? [];

    public IReadOnlyList<UpdateViewConsoleLineDelta> DiffLineDeltas => UpdateView?.Changes.DiffLineDeltas ?? [];

    public IReadOnlyList<UpdateViewConsoleLineDelta> DiagnosticLineDeltas => UpdateView?.Changes.DiagnosticLineDeltas ?? [];

    public string? DisplayKind => Overview?.Latest?.Kind ?? UpdateView?.Header.Kind ?? UpdateView?.Info.Kind;

    public string DisplayKindLabel => Overview?.Latest?.KindLabel ?? UpdateView?.Header.KindLabel ?? WebConsoleDisplayFormatter.FormatUpdateKindLabel(DisplayKind, IsPreview);

    public string DisplayRunLabel => Overview?.Latest?.RunLabel ?? UpdateView?.Header.RunLabel ?? WebConsoleDisplayFormatter.FormatUpdateRunLabel(DisplayKind, IsPreview, DisplayVersion);

    public int? DisplayVersion => UpdateView?.Info.Version ?? Overview?.History.FirstOrDefault(static entry => entry.IsSelected)?.Version;

    public string? DisplayMessage => Overview?.Latest?.Message ?? UpdateView?.Header.Message ?? UpdateView?.Info.Message;

    public long? DisplayStartTime =>
        Overview?.Latest?.StartTime ??
        UpdateView?.Header.StartTime ??
        UpdateView?.Info.StartTime;

    public long? DisplayEndTime =>
        Overview?.Latest?.EndTime ??
        UpdateView?.Header.EndTime ??
        UpdateView?.Info.EndTime;

    public IReadOnlyDictionary<string, int>? DisplayResourceChanges =>
        Overview?.Latest?.ResourceChanges ?? UpdateView?.Header.ResourceChanges ?? UpdateView?.Summary?.ResourceChanges;

    public int? DisplayResourceCount =>
        Overview?.Latest?.ResourceCount ??
        UpdateView?.Header.ResourceCount;

    public string DisplayStartedRelativeText => WebConsoleDisplayFormatter.FormatRelativeTime(DisplayStartTime);

    public string DisplayStartedExactText => WebConsoleDisplayFormatter.FormatExactTime(DisplayStartTime);

    public string DisplayCompletedExactText => WebConsoleDisplayFormatter.FormatExactTime(DisplayEndTime);

    public IReadOnlyList<StackUpdateHistoryEntryViewData> UpdateHistoryEntries =>
        Overview?.History is { Length: > 0 } overviewHistory
            ? overviewHistory.Select(MapUpdateHistoryEntry).ToArray()
            : UpdateView?.History is { Length: > 0 } history
            ? history.Select(MapUpdateHistoryEntry).ToArray()
            : [];

    public StackUpdateHistoryEntryViewData? LatestUpdateHistoryEntry =>
        UpdateHistoryEntries.FirstOrDefault();

    public IReadOnlyList<StackUpdateHistoryEntryViewData> RemainingUpdateHistoryEntries =>
        UpdateHistoryEntries.Skip(1).ToArray();

    public bool ShowUpdateHistory =>
        string.IsNullOrWhiteSpace(SelectedRunType) &&
        string.IsNullOrWhiteSpace(SelectedRouteReference);

    public StackLivePanelViewModel WithForcedTerminalRefresh()
    {
        if (UpdateView is null)
        {
            return this;
        }

        var forcedDelta = new UpdateViewDelta(
            IsIncremental: true,
            InfoChanged: true,
            ChangesChanged: true,
            ConfigurationChanged: true,
            OutputsChanged: true,
            ResourcesChanged: true,
            SummaryChanged: true);

        return this with
        {
            UpdateView = UpdateView with { Delta = forcedDelta }
        };
    }

    public static StackLivePanelViewModel Empty(string org, string project, string stack)
    {
        return new StackLivePanelViewModel(
            org,
            project,
            stack,
            null,
            null,
            null,
            null,
            false,
            "No active update",
            null,
            null);
    }

    private static IReadOnlyList<StackChangeFeedEntryViewModel> MapChangeFeedEntries(UpdateViewChangeEntry[]? entries)
    {
        if (entries is not { Length: > 0 })
        {
            return [];
        }

        return entries.Select(MapChangeFeedEntry).ToArray();
    }

    private static StackChangeFeedEntryViewModel MapChangeFeedEntry(UpdateViewChangeEntry entry)
    {
        var title = entry.Kind switch
        {
            "resource" => BuildResourceTitle(entry),
            "diagnostic" => string.IsNullOrWhiteSpace(entry.Severity) ? "Diagnostic" : entry.Severity,
            "text" => string.IsNullOrWhiteSpace(entry.Channel) ? "Output" : entry.Channel,
            "configuration" => "Configuration",
            "summary" => "Summary",
            _ => string.IsNullOrWhiteSpace(entry.Kind) ? "Change" : entry.Kind
        };

        var detail = entry.Kind switch
        {
            "resource" => BuildResourceDetail(entry),
            "diagnostic" => entry.Text ?? string.Empty,
            "text" => entry.Text ?? string.Empty,
            "configuration" => entry.ConfigEntries is { Length: > 0 }
                ? string.Join(", ", entry.ConfigEntries.Select(static config => $"{config.Key}={config.Value}"))
                : "Configuration updated",
            "summary" => entry.Summary ?? string.Empty,
            _ => entry.Text ?? entry.Info ?? entry.Summary ?? string.Empty
        };

        return new StackChangeFeedEntryViewModel(
            DomId: BuildChangeFeedDomId(entry.Key),
            Key: entry.Key,
            Sequence: entry.Sequence,
            Kind: entry.Kind,
            Action: entry.Action,
            IsError: entry.IsError,
            Title: title,
            Detail: detail,
            Marker: entry.Marker,
            ResourceName: entry.ResourceName,
            ResourceType: entry.ResourceType,
            ResourceUrn: entry.ResourceUrn);
    }

    private static string BuildResourceTitle(UpdateViewChangeEntry entry)
    {
        var operation = string.IsNullOrWhiteSpace(entry.Operation) ? "resource" : entry.Operation;
        if (!string.IsNullOrWhiteSpace(entry.ResourceType) && !string.IsNullOrWhiteSpace(entry.ResourceName))
        {
            return $"{operation}: {entry.ResourceType} {entry.ResourceName}";
        }

        if (!string.IsNullOrWhiteSpace(entry.ResourceType))
        {
            return $"{operation}: {entry.ResourceType}";
        }

        return operation;
    }

    private static string BuildResourceDetail(UpdateViewChangeEntry entry)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(entry.Status))
        {
            parts.Add(entry.Status);
        }

        if (!string.IsNullOrWhiteSpace(entry.Info))
        {
            parts.Add(entry.Info);
        }

        if (!string.IsNullOrWhiteSpace(entry.ResourceUrn))
        {
            parts.Add(entry.ResourceUrn);
        }

        return parts.Count == 0 ? "Resource updated" : string.Join(" · ", parts);
    }

    private static string BuildChangeFeedDomId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return $"live-change-entry-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private StackUpdateHistoryEntryViewData MapUpdateHistoryEntry(UpdateViewHistoryEntry update)
    {
        var href = !string.IsNullOrWhiteSpace(update.RouteRunType) && !string.IsNullOrWhiteSpace(update.RouteReference)
            ? ConsoleRoutePaths.StackRun(Org, Project, Stack, update.RouteRunType, update.RouteReference)
            : null;

        return new StackUpdateHistoryEntryViewData(
            Title: update.Title,
            KindLabel: update.KindLabel,
            RunLabel: update.RunLabel,
            StatusText: update.Status,
            StartedRelativeText: WebConsoleDisplayFormatter.FormatRelativeTime(update.StartTime),
            StartedExactText: WebConsoleDisplayFormatter.FormatExactTime(update.StartTime),
            Message: update.Message,
            Href: href,
            IsSelected: update.IsSelected);
    }

}

public sealed record StackChangeFeedEntryViewModel(
    string DomId,
    string Key,
    long Sequence,
    string Kind,
    string Action,
    bool IsError,
    string Title,
    string Detail,
    string? Marker,
    string? ResourceName,
    string? ResourceType,
    string? ResourceUrn);
