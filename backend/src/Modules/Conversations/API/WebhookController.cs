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
            var normalizedSender = NormalizeWhatsAppPhone(payload.Sender);
            var senderLid = string.IsNullOrWhiteSpace(payload.SenderLid) ? null : payload.SenderLid.Trim();

            // 1. Resolve Customer by PhoneNumber globally but within payload Project.
            // WhatsApp multi-device may send a @lid as remoteJid and the real phone in remoteJidAlt.
            var customer = await _context.Customers
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c =>
                    c.ProjectId == payload.ProjectId &&
                    (c.PhoneNumber == normalizedSender || (senderLid != null && c.PhoneNumber == senderLid)));

            if (customer == null)
            {
                customer = new Customer
                {
                    ProjectId = payload.ProjectId,
                    PhoneNumber = normalizedSender,
                    Name = !string.IsNullOrWhiteSpace(payload.Name) 
                        ? payload.Name 
                        : $"WA Customer {normalizedSender.Substring(Math.Max(0, normalizedSender.Length - 4))}",
                    City = string.Empty,
                    Notes = string.Empty
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }
            else if (customer.PhoneNumber.EndsWith("@lid", StringComparison.OrdinalIgnoreCase) && normalizedSender != customer.PhoneNumber)
            {
                customer.PhoneNumber = normalizedSender;
                if (!string.IsNullOrWhiteSpace(payload.Name))
                {
                    customer.Name = payload.Name;
                }
                _context.Entry(customer).State = EntityState.Modified;
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

            // 3.6 Broadcast AI typing if auto-reply is enabled
            var settings = await _context.ProjectSettings
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(s => s.ProjectId == payload.ProjectId);
            if (settings != null && settings.AiAutoReplyEnabled)
            {
                await _hubContext.Clients.Group($"project_{payload.ProjectId}").SendAsync("AITyping", new
                {
                    conversationId = conversation.Id,
                    isTyping = true
                });
            }

            // 4. Pass message to aggregator for grouping window
            await _messageAggregator.AggregateMessageAsync(payload.ProjectId, normalizedSender, payload.Content);

            return Ok(new { status = "Received" });
        }

        private static string NormalizeWhatsAppPhone(string sender)
        {
            if (string.IsNullOrWhiteSpace(sender))
            {
                return sender;
            }

            var trimmed = sender.Trim();
            if (trimmed.EndsWith("@s.whatsapp.net", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Split('@')[0];
            }

            return trimmed;
        }
    }

    public class IncomingMessagePayload
    {
        public Guid ProjectId { get; set; }
        public string MessageId { get; set; } = default!;
        public string Sender { get; set; } = default!;
        public string? SenderJid { get; set; }
        public string? SenderLid { get; set; }
        public string? Name { get; set; }
        public string Content { get; set; } = default!;
        public string? MessageType { get; set; }
        public long Timestamp { get; set; }
    }
}
