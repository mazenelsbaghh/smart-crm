using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;
using Shared.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.Conversations.Services
{
    public class AssignmentEngine : IAssignmentEngine
    {
        private readonly IDatabase _redis;
        private readonly AppDbContext _context;

        public AssignmentEngine(IConnectionMultiplexer redis, AppDbContext context)
        {
            _redis = redis.GetDatabase();
            _context = context;
        }

        public async Task UpdatePresenceAsync(Guid projectId, Guid agentId, bool isOnline)
        {
            var presenceKey = $"project:{projectId}:agent:{agentId}:presence";

            // Count active open conversations in DB for this agent
            var activeCount = await _context.Conversations
                .IgnoreQueryFilters()
                .CountAsync(c => c.ProjectId == projectId && c.AssignedUserId == agentId && c.Status == "Open");

            var hashEntries = new HashEntry[]
            {
                new HashEntry("IsOnline", isOnline.ToString().ToLower()),
                new HashEntry("LastActive", DateTime.UtcNow.ToString("o")),
                new HashEntry("ActiveConversationsCount", activeCount.ToString())
            };

            await _redis.HashSetAsync(presenceKey, hashEntries);
            await _redis.KeyExpireAsync(presenceKey, TimeSpan.FromSeconds(60));
        }

        public async Task<List<AgentWorkloadItem>> GetWorkloadReportAsync(Guid projectId)
        {
            // Retrieve all users belonging to this project
            var users = await _context.Users
                .IgnoreQueryFilters()
                .Where(u => u.ProjectId == projectId)
                .ToListAsync();

            var report = new List<AgentWorkloadItem>();

            foreach (var user in users)
            {
                var presenceKey = $"project:{projectId}:agent:{user.Id}:presence";
                var hash = await _redis.HashGetAllAsync(presenceKey);

                bool isOnline = false;
                int activeCount = 0;

                if (hash != null && hash.Length > 0)
                {
                    var isOnlineEntry = hash.FirstOrDefault(h => h.Name == "IsOnline");
                    var countEntry = hash.FirstOrDefault(h => h.Name == "ActiveConversationsCount");

                    if (isOnlineEntry.Value.HasValue)
                    {
                        isOnline = isOnlineEntry.Value.ToString() == "true";
                    }
                    if (countEntry.Value.HasValue && int.TryParse(countEntry.Value.ToString(), out var count))
                    {
                        activeCount = count;
                    }
                }
                else
                {
                    // If not in Redis, count from DB
                    activeCount = await _context.Conversations
                        .IgnoreQueryFilters()
                        .CountAsync(c => c.ProjectId == projectId && c.AssignedUserId == user.Id && c.Status == "Open");
                }

                report.Add(new AgentWorkloadItem
                {
                    AgentId = user.Id,
                    AgentName = user.Email, // Email used as name
                    IsOnline = isOnline,
                    ActiveConversationsCount = activeCount
                });
            }

            return report;
        }

        public async Task<Guid?> AssignConversationAsync(Guid projectId, Guid conversationId, Guid? agentId = null)
        {
            var conversation = await _context.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.Id == conversationId);

            if (conversation == null)
            {
                return null;
            }

            Guid? previousAgentId = conversation.AssignedUserId;

            if (agentId.HasValue)
            {
                // Direct assignment
                var agentExists = await _context.Users
                    .IgnoreQueryFilters()
                    .AnyAsync(u => u.ProjectId == projectId && u.Id == agentId.Value);

                if (!agentExists)
                {
                    throw new ArgumentException("Agent does not exist in this project");
                }

                conversation.AssignedUserId = agentId.Value;
            }
            else
            {
                var customer = await _context.Customers
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ProjectId == projectId && c.Id == conversation.CustomerId);

                var usersQuery = _context.Users
                    .IgnoreQueryFilters()
                    .Where(u => u.ProjectId == projectId);

                if (customer != null && customer.LeadScore >= 80)
                {
                    Console.WriteLine($"[AssignmentEngine] VIP customer detected (LeadScore: {customer.LeadScore}). Filtering to Admin/Owner role.");
                    usersQuery = usersQuery.Where(u => u.Role == "Admin" || u.Role == "Owner");
                }
                else if (conversation.Status == "Pending" || (customer != null && (customer.Label == "شكوى" || customer.Label == "Complaint")))
                {
                    Console.WriteLine($"[AssignmentEngine] Complaint/Pending conversation detected. Filtering to Supervisor/Admin role.");
                    usersQuery = usersQuery.Where(u => u.Role == "Supervisor" || u.Role == "Admin");
                }

                var eligibleUsers = await usersQuery.ToListAsync();

                if (!eligibleUsers.Any())
                {
                    eligibleUsers = await _context.Users
                        .IgnoreQueryFilters()
                        .Where(u => u.ProjectId == projectId)
                        .ToListAsync();
                }

                var onlineAgents = new List<(Guid Id, int Count)>();

                foreach (var user in eligibleUsers)
                {
                    var presenceKey = $"project:{projectId}:agent:{user.Id}:presence";
                    var isOnlineVal = await _redis.HashGetAsync(presenceKey, "IsOnline");

                    if (isOnlineVal.HasValue && isOnlineVal.ToString() == "true")
                    {
                        var countVal = await _redis.HashGetAsync(presenceKey, "ActiveConversationsCount");
                        int count = 0;
                        if (countVal.HasValue && int.TryParse(countVal.ToString(), out var parsedCount))
                        {
                            count = parsedCount;
                        }
                        else
                        {
                            count = await _context.Conversations
                                .IgnoreQueryFilters()
                                .CountAsync(c => c.ProjectId == projectId && c.AssignedUserId == user.Id && c.Status == "Open");
                        }
                        onlineAgents.Add((user.Id, count));
                    }
                }

                if (onlineAgents.Any())
                {
                    var chosenAgent = onlineAgents.OrderBy(a => a.Count).First();
                    conversation.AssignedUserId = chosenAgent.Id;
                }
                else
                {
                    if (eligibleUsers.Any())
                    {
                        conversation.AssignedUserId = eligibleUsers.First().Id;
                        Console.WriteLine($"[AssignmentEngine] No online agents available. Assigned to offline eligible user: {conversation.AssignedUserId}");
                    }
                    else
                    {
                        Console.WriteLine($"[AssignmentEngine] No agents found at all to assign conversation {conversationId}");
                        return null;
                    }
                }
            }

            conversation.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Refresh Redis workloads for affected agents
            if (conversation.AssignedUserId.HasValue)
            {
                var isOnlineVal = await _redis.HashGetAsync($"project:{projectId}:agent:{conversation.AssignedUserId.Value}:presence", "IsOnline");
                bool isOnline = isOnlineVal.HasValue && isOnlineVal.ToString() == "true";
                await UpdatePresenceAsync(projectId, conversation.AssignedUserId.Value, isOnline);
            }

            if (previousAgentId.HasValue && previousAgentId.Value != conversation.AssignedUserId)
            {
                var isOnlineVal = await _redis.HashGetAsync($"project:{projectId}:agent:{previousAgentId.Value}:presence", "IsOnline");
                bool isOnline = isOnlineVal.HasValue && isOnlineVal.ToString() == "true";
                await UpdatePresenceAsync(projectId, previousAgentId.Value, isOnline);
            }

            return conversation.AssignedUserId;
        }
    }
}
