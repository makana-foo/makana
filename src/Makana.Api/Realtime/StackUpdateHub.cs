using Microsoft.AspNetCore.SignalR;

namespace Makana.Api.Realtime;

public sealed class StackUpdateHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var org = httpContext?.Request.Query["org"].ToString();
        var project = httpContext?.Request.Query["project"].ToString();
        var stack = httpContext?.Request.Query["stack"].ToString();
        var updateId = httpContext?.Request.Query["updateId"].ToString();

        if (string.IsNullOrWhiteSpace(org) ||
            string.IsNullOrWhiteSpace(project) ||
            string.IsNullOrWhiteSpace(stack) ||
            string.IsNullOrWhiteSpace(updateId))
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, StackUpdateGroups.For(org, project, stack, updateId));

        await base.OnConnectedAsync();
    }
}
