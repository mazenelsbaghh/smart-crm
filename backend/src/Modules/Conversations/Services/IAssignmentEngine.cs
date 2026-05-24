using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Modules.Conversations.Services
{
    public class AgentWorkloadItem
    {
        public Guid AgentId { get; set; }
        public string AgentName { get; set; }
        public bool IsOnline { get; set; }
        public int ActiveConversationsCount { get; set; }
    }

    public interface IAssignmentEngine
    {
        Task UpdatePresenceAsync(Guid projectId, Guid agentId, bool isOnline);
        Task<List<AgentWorkloadItem>> GetWorkloadReportAsync(Guid projectId);
        Task<Guid?> AssignConversationAsync(Guid projectId, Guid conversationId, Guid? agentId = null);
    }
}
