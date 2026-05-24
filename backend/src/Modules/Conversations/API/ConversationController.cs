using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.Conversations.API
{
    [ApiController]
    [Route("api")]
    public class ConversationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Services.IAssignmentEngine _assignmentEngine;
        private readonly Shared.Queue.IEventBus _eventBus;

        public ConversationController(AppDbContext context, Services.IAssignmentEngine assignmentEngine, Shared.Queue.IEventBus eventBus)
        {
            _context = context;
            _assignmentEngine = assignmentEngine;
            _eventBus = eventBus;
        }

        [HttpGet("projects/{projectId}/conversations")]
        public async Task<IActionResult> ListConversations(Guid projectId)
        {
            var conversations = await _context.Conversations
                .Select(c => new
                {
                    c.Id,
                    c.ProjectId,
                    c.CustomerId,
                    c.AssignedUserId,
                    c.Status,
                    c.LastMessageTimestamp,
                    c.CreatedAt
                })
                .ToListAsync();

            return Ok(conversations);
        }

        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> ListMessages(Guid conversationId)
        {
            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            return Ok(messages);
        }

        [HttpPost("conversations/{id}/assign")]
        public async Task<IActionResult> AssignConversation(Guid id, [FromBody] AssignConversationRequest request, [FromHeader(Name = "X-Project-Id")] Guid? projectIdHeader)
        {
            var projectId = projectIdHeader ?? _context.CurrentProjectId;
            if (projectId == Guid.Empty)
            {
                return BadRequest("Project Context Required");
            }

            try
            {
                var assignedAgentId = await _assignmentEngine.AssignConversationAsync(projectId, id, request?.AgentId);
                return Ok(new { conversationId = id, assignedUserId = assignedAgentId });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("projects/{projectId}/agents/{agentId}/presence")]
        public async Task<IActionResult> UpdatePresence(Guid projectId, Guid agentId, [FromBody] PresenceUpdateRequest request)
        {
            await _assignmentEngine.UpdatePresenceAsync(projectId, agentId, request.IsOnline);
            return Ok(new { projectId, agentId, request.IsOnline });
        }

        [HttpGet("projects/{projectId}/agents/workload")]
        public async Task<IActionResult> GetWorkloadReport(Guid projectId)
        {
            var report = await _assignmentEngine.GetWorkloadReportAsync(projectId);
            return Ok(report);
        }

        [HttpPut("conversations/{id}/status")]
        public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateConversationStatusRequest request)
        {
            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null) return NotFound();

            var oldStatus = conversation.Status;
            conversation.Status = request.Status;
            await _context.SaveChangesAsync();

            if (request.Status.Equals("Closed", StringComparison.OrdinalIgnoreCase) && 
                !oldStatus.Equals("Closed", StringComparison.OrdinalIgnoreCase))
            {
                await _eventBus.PublishAsync(new Shared.Events.ConversationClosedEvent
                {
                    ProjectId = conversation.ProjectId,
                    CustomerId = conversation.CustomerId,
                    ConversationId = conversation.Id
                });
            }

            return Ok(conversation);
        }
    }

    public class AssignConversationRequest
    {
        public Guid? AgentId { get; set; }
    }

    public class PresenceUpdateRequest
    {
        public bool IsOnline { get; set; }
    }

    public class UpdateConversationStatusRequest
    {
        public string Status { get; set; } = string.Empty;
    }
}
