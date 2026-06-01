using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Modules.Conversations.Hubs;
using Modules.Conversations.Domain;

using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Modules.Conversations.API
{
    [ApiController]
    [Route("api")]
    public class ConversationController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly Services.IAssignmentEngine _assignmentEngine;
        private readonly Shared.Queue.IEventBus _eventBus;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public ConversationController(
            AppDbContext context, 
            Services.IAssignmentEngine assignmentEngine, 
            Shared.Queue.IEventBus eventBus, 
            IHubContext<NotificationHub> hubContext,
            IConfiguration configuration)
        {
            _context = context;
            _assignmentEngine = assignmentEngine;
            _eventBus = eventBus;
            _hubContext = hubContext;
            _configuration = configuration;
            _httpClient = new HttpClient();
        }

        [HttpGet("projects/{projectId}/conversations")]
        public async Task<IActionResult> ListConversations(Guid projectId)
        {
            var conversations = await _context.Conversations
                .Where(c => c.ProjectId == projectId)
                .Join(_context.Customers,
                    c => c.CustomerId,
                    cust => cust.Id,
                    (c, cust) => new
                    {
                        c.Id,
                        c.ProjectId,
                        c.Status,
                        c.LastMessageTimestamp,
                        c.CreatedAt,
                        c.AssignedUserId,
                        customer = new
                        {
                            id = cust.Id,
                            name = cust.Name ?? cust.PhoneNumber,
                            phone = cust.PhoneNumber,
                            avatarUrl = (string)null,
                            label = cust.Label
                        }
                    })
                .ToListAsync();

            var mapped = conversations.Select(c => new
            {
                id = c.Id,
                projectId = c.ProjectId,
                status = c.Status,
                lastMessageAt = c.LastMessageTimestamp.ToString("o"),
                createdAt = c.CreatedAt.ToString("o"),
                unreadCount = 0,
                assignedAgentId = c.AssignedUserId,
                assignedAgentName = (string)null,
                customer = c.customer
            }).ToList();

            return Ok(mapped);
        }

        [HttpGet("conversations/{conversationId}/messages")]
        public async Task<IActionResult> ListMessages(Guid conversationId)
        {
            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Timestamp)
                .Select(m => new
                {
                    id = m.Id,
                    conversationId = m.ConversationId,
                    senderType = m.Direction == "Incoming" ? "Customer" : (m.ExternalMessageId != null && m.ExternalMessageId.StartsWith("msg_ai_") ? "AI" : "Agent"),
                    content = m.Content,
                    createdAt = m.Timestamp.ToString("o"),
                    status = m.Direction == "Incoming" ? "Delivered" : "Sent",
                    mediaUrl = (string)null,
                    mediaType = (string)null,
                    direction = m.Direction,
                    timestamp = m.Timestamp
                })
                .ToListAsync();

            return Ok(messages);
        }

        [HttpPost("conversations/{id}/messages")]
        public async Task<IActionResult> SendMessage(Guid id, [FromBody] SendMessageRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest("Content is required.");
            }

            var conversation = await _context.Conversations.FindAsync(id);
            if (conversation == null)
            {
                return NotFound($"Conversation {id} not found.");
            }

            // Create Outgoing message
            var message = new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = $"msg_agent_{Guid.NewGuid().ToString("N")}",
                Direction = "Outgoing",
                Content = request.Content,
                MessageType = "Text",
                Timestamp = DateTime.UtcNow
            };

            _context.Messages.Add(message);
            
            // Update conversation last message timestamp
            conversation.LastMessageTimestamp = DateTime.UtcNow;
            _context.Entry(conversation).State = EntityState.Modified;

            await _context.SaveChangesAsync();

            // Broadcast message via SignalR to project group
            var payload = new
            {
                id = message.Id,
                conversationId = message.ConversationId,
                senderType = "Agent",
                content = message.Content,
                createdAt = message.Timestamp.ToString("o"),
                status = "Sent",
                mediaUrl = (string)null,
                mediaType = (string)null
            };

            await _hubContext.Clients.Group($"project_{conversation.ProjectId}").SendAsync("ReceiveMessage", payload);

            // Forward to WhatsApp Gateway
            var customer = await _context.Customers.FindAsync(conversation.CustomerId);
            if (customer != null && !string.IsNullOrEmpty(customer.PhoneNumber))
            {
                var gatewayUrl = _configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
                var gatewayPayload = new
                {
                    projectId = conversation.ProjectId,
                    to = customer.PhoneNumber,
                    message = request.Content
                };
                
                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(gatewayPayload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    Encoding.UTF8,
                    "application/json"
                );

                try
                {
                    var response = await _httpClient.PostAsync($"{gatewayUrl}/api/whatsapp/send", jsonContent);
                    if (!response.IsSuccessStatusCode)
                    {
                        var responseBody = await response.Content.ReadAsStringAsync();
                        Console.WriteLine($"[ConversationController] Gateway returned error code {response.StatusCode}: {responseBody}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ConversationController] Exception while calling WhatsApp Gateway: {ex.Message}");
                }
            }

            return Ok(payload);
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

    public class SendMessageRequest
    {
        public string Content { get; set; } = string.Empty;
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
