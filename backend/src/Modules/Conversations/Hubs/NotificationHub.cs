using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace Modules.Conversations.Hubs
{
    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var projectIdStr = httpContext?.Request.Query["projectId"].ToString();

            // If not in query, try reading from ProjectId claim (JWT connection)
            if (string.IsNullOrEmpty(projectIdStr))
            {
                projectIdStr = Context.User?.FindFirst("ProjectId")?.Value;
            }

            if (Guid.TryParse(projectIdStr, out var projectId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"project_{projectId}");
                Console.WriteLine($"[NotificationHub] Client connected. Assigned to SignalR group: project_{projectId}");
            }
            else
            {
                Console.WriteLine("[NotificationHub] Client connected without a valid ProjectId context.");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var httpContext = Context.GetHttpContext();
            var projectIdStr = httpContext?.Request.Query["projectId"].ToString();

            if (string.IsNullOrEmpty(projectIdStr))
            {
                projectIdStr = Context.User?.FindFirst("ProjectId")?.Value;
            }

            if (Guid.TryParse(projectIdStr, out var projectId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project_{projectId}");
                Console.WriteLine($"[NotificationHub] Client disconnected from group: project_{projectId}");
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
