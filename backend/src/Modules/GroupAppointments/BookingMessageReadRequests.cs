using Microsoft.EntityFrameworkCore;
using Modules.GroupAppointments.Domain;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.GroupAppointments.Services;

public static class BookingMessageReadRequests
{
    public static async Task EnqueueAsync(AppDbContext db, GroupAppointmentBooking booking,
        GroupBookingCommand command, CancellationToken cancellationToken)
    {
        var conversations = db.Conversations.IgnoreQueryFilters().Where(conversation =>
            conversation.ProjectId == booking.ProjectId && conversation.CustomerId == booking.CustomerId
            && conversation.Channel == "WhatsApp" && conversation.WhatsAppDestinationId == null
            && conversation.WhatsAppAccountId != null && conversation.Status != "Closed");
        if (command.SourceConversationId.HasValue)
            conversations = conversations.Where(conversation => conversation.Id == command.SourceConversationId);
        var lastIncoming = await db.Messages.IgnoreQueryFilters()
            .Join(conversations, message => message.ConversationId, conversation => conversation.Id,
                (message, conversation) => message)
            .Where(message => message.Direction == "Incoming" && message.MessageType != "Reaction" && message.ExternalMessageId != "")
            .OrderByDescending(message => message.Timestamp)
            .ThenByDescending(message => message.ReceivedAtUtc ?? message.Timestamp)
            .ThenByDescending(message => message.Id)
            .Select(message => new { message.Id, message.ConversationId }).FirstOrDefaultAsync(cancellationToken);
        if (lastIncoming is null) return;
        IntegrationOutbox.Enqueue(db, new BookingLastMessageReadRequested
        {
            ProjectId = booking.ProjectId, BookingId = booking.Id, CustomerId = booking.CustomerId,
            ConversationId = lastIncoming.ConversationId, MessageId = lastIncoming.Id
        });
    }
}
