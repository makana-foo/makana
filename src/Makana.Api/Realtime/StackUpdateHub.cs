using Microsoft.AspNetCore.SignalR;

namespace Makana.Api.Realtime;

public sealed class StackUpdateHub : Hub
{
    public Task JoinStackUpdate(string org, string project, string stack, string updateId)
    {
        return Groups.AddToGroupAsync(Context.ConnectionId, StackUpdateGroups.For(org, project, stack, updateId));
    }

    public Task LeaveStackUpdate(string org, string project, string stack, string updateId)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, StackUpdateGroups.For(org, project, stack, updateId));
    }
}
