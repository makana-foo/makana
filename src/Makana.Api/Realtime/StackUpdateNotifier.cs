using Microsoft.AspNetCore.SignalR;

namespace Makana.Api.Realtime;

public interface IStackUpdateNotifier
{
    Task NotifyChangedAsync(StackUpdateChangedMessage message, CancellationToken cancellationToken);
}

public sealed record StackUpdateChangedMessage(
    string Org,
    string Project,
    string Stack,
    string UpdateId,
    string Reason,
    DateTimeOffset ChangedAtUtc);

public sealed class SignalRStackUpdateNotifier(
    IHubContext<StackUpdateHub> hubContext,
    ILogger<SignalRStackUpdateNotifier> logger) : IStackUpdateNotifier
{
    public async Task NotifyChangedAsync(StackUpdateChangedMessage message, CancellationToken cancellationToken)
    {
        var group = StackUpdateGroups.For(message.Org, message.Project, message.Stack, message.UpdateId);
        try
        {
            await hubContext.Clients.Group(group).SendAsync("stack-update-changed", message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to publish stack-update-changed for {Org}/{Project}/{Stack} update {UpdateId}",
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
