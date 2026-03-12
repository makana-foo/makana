using Microsoft.AspNetCore.SignalR;
using Makana.Api.Contracts;
using Makana.Api.Updates;

namespace Makana.Api.Realtime;

public interface IStackUpdateNotifier
{
    Task NotifyChangedAsync(StackUpdateChangedMessage message, CancellationToken cancellationToken = default);
}

public sealed record StackUpdateChangedMessage(
    string Org,
    string Project,
    string Stack,
    string StackId,
    string UpdateId,
    long? AfterSequence,
    string Reason,
    DateTimeOffset ChangedAtUtc);

internal sealed class SignalRStackUpdateNotifier(
    IHubContext<StackUpdateHub> hubContext,
    UpdateViewMaterializer updateViewMaterializer,
    ILogger<SignalRStackUpdateNotifier> logger) : IStackUpdateNotifier
{
    public async Task NotifyChangedAsync(StackUpdateChangedMessage message, CancellationToken cancellationToken = default)
    {
        var group = StackUpdateGroups.For(message.Org, message.Project, message.Stack, message.UpdateId);
        try
        {
            var view = await updateViewMaterializer.BuildLiveAsync(
                message.StackId,
                message.UpdateId,
                message.AfterSequence,
                cancellationToken);
            if (view is null)
            {
                return;
            }

            await hubContext.Clients.Group(group).SendAsync(
                "stack-update-view",
                LiveUpdateViewMessage.FromResponse(view),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish stack-update-view for {Org}/{Project}/{Stack} update {UpdateId}",
                message.Org,
                message.Project,
                message.Stack,
                message.UpdateId);
        }
    }
}

internal static class StackUpdateGroups
{
    public static string For(string org, string project, string stack, string updateId)
    {
        return $"stack-update::{org}/{project}/{stack}/{updateId}";
    }
}
