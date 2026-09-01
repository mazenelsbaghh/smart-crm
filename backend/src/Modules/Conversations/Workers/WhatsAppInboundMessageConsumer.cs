using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;

namespace Modules.Conversations.Workers;

public sealed class WhatsAppInboundMessageConsumer(AppDbContext db, IAdvertisingReferralProtector protector,
    WhatsAppInboundEventPublisher publisher) : IntegrationProjectionConsumer<WhatsAppInboundMessageReceived>(db),
    IIntegrationEventHandler<WhatsAppInboundMessageReceived>
{
    protected override string ConsumerName => nameof(WhatsAppInboundMessageConsumer);

    public Task HandleAsync(WhatsAppInboundMessageReceived message) => ConsumeAsync(message, async cancellationToken =>
    {
        if (await Db.Messages.IgnoreQueryFilters().AnyAsync(item => item.ExternalMessageId == message.ProviderMessageId, cancellationToken)) return;
        var sender = protector.UnprotectInboundJson(message.ProtectedSenderReference);
        var content = JsonSerializer.Deserialize<NormalizedInbound>(message.NormalizedContentJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(string.Empty, string.Empty, "Text");
        var customer = await Db.Customers.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.ProjectId == message.ProjectId && item.PhoneNumber == sender, cancellationToken);
        if (customer is null)
        {
            customer = new Customer { ProjectId = message.ProjectId, PhoneNumber = sender,
                Name = string.IsNullOrWhiteSpace(content.Name) ? $"WA Customer {sender[^Math.Min(4, sender.Length)..]}" : content.Name };
            Db.Customers.Add(customer);
        }
        var conversation = await Db.Conversations.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.ProjectId == message.ProjectId
            && item.CustomerId == customer.Id
            && item.Channel == "WhatsApp"
            && item.WhatsAppAccountId == null
            && item.WhatsAppDestinationId == message.DestinationId
            && item.Status != "Closed", cancellationToken);
        if (conversation is null)
        {
            conversation = new Conversation { ProjectId = message.ProjectId, CustomerId = customer.Id,
                WhatsAppDestinationId = message.DestinationId, Status = "Open", Channel = "WhatsApp",
                LastMessageTimestamp = message.MessageOccurredAtUtc };
            Db.Conversations.Add(conversation);
        }
        else conversation.LastMessageTimestamp = message.MessageOccurredAtUtc;
        var isFirst = !await Db.Messages.IgnoreQueryFilters().AnyAsync(item => item.ConversationId == conversation.Id, cancellationToken);
        Db.Messages.Add(new Message { ConversationId = conversation.Id, ExternalMessageId = message.ProviderMessageId,
            Direction = "Incoming", Content = content.Content, MessageType = content.MessageType, Timestamp = message.MessageOccurredAtUtc });
        var referralJson = protector.UnprotectInboundJson(message.ProtectedReferralJson);
        var referral = JsonSerializer.Deserialize<InboundAdvertisingContext>(referralJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new("Missing", null, null, null, "CloudApi");
        publisher.PublishObservation(message.ProjectId, conversation.Id, customer.Id, message.DestinationId,
            message.DestinationVersion, message.ProviderMessageId, message.MessageOccurredAtUtc, referral, isFirst);
    });

    private sealed record NormalizedInbound(string Name, string Content, string MessageType);
}
