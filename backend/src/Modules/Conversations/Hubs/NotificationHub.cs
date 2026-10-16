using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Shared.Security;

namespace Modules.Conversations.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    private Guid ProjectId => new ProjectAuthorizationService().GetProjectId(Context.User!)
        ?? throw new HubException("Project access is required.");

    public override async Task OnConnectedAsync()
    {
        var requested = Context.GetHttpContext()?.Request.Query["projectId"].ToString();
        if (!string.IsNullOrEmpty(requested) && (!Guid.TryParse(requested, out var id) || id != ProjectId))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project_{ProjectId}");
        await base.OnConnectedAsync();
    }

    public Task JoinProjectGroup(string projectIdStr)
    {
        if (!Guid.TryParse(projectIdStr, out var projectId) || projectId != ProjectId)
            throw new HubException("Project access denied.");
        return Groups.AddToGroupAsync(Context.ConnectionId, $"project_{ProjectId}");
    }

    public Task UpdatePresence(string status) =>
        Clients.Group($"project_{ProjectId}").SendAsync("AgentPresenceUpdated", Context.UserIdentifier, status);
}
