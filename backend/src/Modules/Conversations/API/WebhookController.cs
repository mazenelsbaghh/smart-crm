using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Modules.Conversations.Hubs;
using Microsoft.AspNetCore.SignalR;
using Shared.Infrastructure;
using Shared.Security;
using System;
using System.Threading.Tasks;

namespace Modules.Conversations.API
{
    [ApiController]
    [Route("api/webhooks/whatsapp")]
    public class WebhookController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly ITenantContext _tenantContext;
        private readonly IMessageAggregator _messageAggregator;
        private readonly IHubContext<NotificationHub> _hubContext;

        public WebhookController(AppDbContext context, ITenantContext tenantContext, IMessageAggregator messageAggregator, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _tenantContext = tenantContext;
            _messageAggregator = messageAggregator;
            _hubContext = hubContext;
        }

        [HttpPost("message")]
        public async Task<IActionResult> ReceiveMessage([FromBody] IncomingMessagePayload payload)
        {
            // Set context tenant project id
            _tenantContext.SetProjectId(payload.ProjectId);

            // 1. Resolve Customer by PhoneNumber globally but within payload Project
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == payload.ProjectId && c.PhoneNumber == payload.Sender);

            if (customer == null)
            {
                customer = new Customer
                {
                    ProjectId = payload.ProjectId,
                    PhoneNumber = payload.Sender,
                    Name = $"WA Customer {payload.Sender.Substring(Math.Max(0, payload.Sender.Length - 4))}",
                    City = string.Empty,
                    Notes = string.Empty
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }

            // 2. Resolve or create active Conversation thread
            var conversation = await _context.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == payload.ProjectId && c.CustomerId == customer.Id && c.Status != "Closed");

            if (conversation == null)
            {
                conversation = new Conversation
                {
                    ProjectId = payload.ProjectId,
                    CustomerId = customer.Id,
                    Status = "Open",
                    LastMessageTimestamp = DateTime.UtcNow
                };
                _context.Conversations.Add(conversation);
                await _context.SaveChangesAsync();
            }
            else
            {
                conversation.LastMessageTimestamp = DateTime.UtcNow;
                _context.Entry(conversation).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }

            // 3. Save individual incoming message
            var message = new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = payload.MessageId,
                Direction = "Incoming",
                Content = payload.Content,
                MessageType = payload.MessageType ?? "Text",
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(payload.Timestamp).UtcDateTime
            };

            _context.Messages.Add(message);
            await _context.SaveChangesAsync();

            // 3.5 Broadcast via SignalR to the group
            await _hubContext.Clients.Group($"project_{payload.ProjectId}").SendAsync("ReceiveMessage", new
            {
                id = message.Id,
                conversationId = message.ConversationId,
                senderType = "Customer",
                content = message.Content,
                createdAt = message.Timestamp.ToString("o"),
                status = "Delivered",
                mediaUrl = (string)null,
                mediaType = (string)null
            });

            // 4. Pass message to aggregator for grouping window
            await _messageAggregator.AggregateMessageAsync(payload.ProjectId, payload.Sender, payload.Content);

            return Ok(new { status = "Received" });
        }
    }

    public class IncomingMessagePayload
    {
        public Guid ProjectId { get; set; }
        public string MessageId { get; set; }
        public string Sender { get; set; }
        public string Content { get; set; }
        public string MessageType { get; set; }
        public long Timestamp { get; set; }
    }
}
