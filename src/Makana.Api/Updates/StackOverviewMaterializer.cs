using Makana.Api.Contracts;
using Makana.Api.Endpoints;
using Makana.Api.Storage;

namespace Makana.Api.Updates;

internal sealed class StackOverviewMaterializer(
    BackendStore store,
    EndpointHelpers endpointHelpers)
{
    public async Task<StackOverviewResponse> BuildAsync(string stackId, CancellationToken cancellationToken)
    {
        var stack = endpointHelpers.BuildStackPageMetadata(
            await store.GetStackPageMetadataByIdAsync(stackId, cancellationToken));
        var updates = (await store.GetHistoryAsync(stackId, includePreviews: true))
            .OrderByDescending(static update => ParseSortTimestamp(update.CreatedAt))
            .ThenByDescending(static update => ParseSortTimestamp(update.CompletedAt))
            .ToArray();
        if (updates.Length == 0)
        {
            return new StackOverviewResponse(stack, null, []);
        }

        var previewFlags = await store.GetUpdatePreviewFlagsAsync(updates.Select(static update => update.Id).ToArray(), cancellationToken);
        var resourceCount = await endpointHelpers.GetResourceCountAsync(store, stackId);
        var history = updates.Select(update =>
            {
                var isPreview = endpointHelpers.IsPreviewOperation(
                    update.Kind,
                    update.ProgramRequestJson,
                    previewFlags.GetValueOrDefault(update.Id));
                var (message, _) = endpointHelpers.ExtractMetadata(update.ProgramRequestJson);
                return new UpdateViewHistoryEntry(
                    UpdateId: update.Id,
                    Version: update.Version,
                    Kind: update.Kind,
                    IsPreview: isPreview,
                    Title: endpointHelpers.FormatUpdateHistoryTitle(update.Version),
                    KindLabel: endpointHelpers.FormatUpdateKindLabel(update.Kind, isPreview),
                    RunLabel: endpointHelpers.FormatUpdateRunLabel(update.Kind, isPreview, update.Version),
                    Status: endpointHelpers.NormalizeUpdateStatus(update.Status),
                    StartTime: endpointHelpers.ToUnixTimeSeconds(update.CreatedAt),
                    Message: message,
                    RouteRunType: endpointHelpers.GetUpdateRouteRunType(update.Kind, isPreview),
                    RouteReference: endpointHelpers.GetUpdateRouteReference(update.Id, update.Version, update.Kind, isPreview),
                    IsSelected: false);
            })
            .ToArray();

        var latest = history[0];
        var latestHeader = new UpdateViewHeader(
            Kind: latest.Kind,
            IsPreview: latest.IsPreview,
            KindLabel: latest.KindLabel,
            RunLabel: latest.RunLabel,
            Status: latest.Status,
            Message: latest.Message,
            StartTime: latest.StartTime,
            EndTime: endpointHelpers.ToUnixTimeSeconds(updates[0].CompletedAt),
            ResourceCount: resourceCount,
            ResourceChanges: null);

        return new StackOverviewResponse(stack, latestHeader, history);
    }

    private static DateTimeOffset ParseSortTimestamp(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }
}
