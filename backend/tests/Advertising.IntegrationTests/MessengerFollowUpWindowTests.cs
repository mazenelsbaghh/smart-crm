using Microsoft.EntityFrameworkCore;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Modules.Projects.Domain;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MessengerFollowUpWindowTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Production_2026_09_28_recent_outgoing_message_does_not_reopen_Messenger_follow_up_window()
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var nowUtc = new DateTime(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
        var project = new Project { Name = "Messenger follow-up window regression" };
        var customer = new Customer
        {
            ProjectId = project.Id,
            Name = "عميل",
            City = "",
            PhoneNumber = "201099999999",
            FacebookPSID = "messenger-regression-psid"
        };
        var conversation = new Conversation
        {
            ProjectId = project.Id,
            CustomerId = customer.Id,
            Channel = "Messenger",
            LastMessageTimestamp = nowUtc.AddMinutes(-1)
        };
        var followUp = new FollowUp
        {
            ProjectId = project.Id,
            CustomerId = customer.Id,
            ConversationId = conversation.Id,
            Channel = "Messenger",
            DueDate = nowUtc,
            Notes = "متابعة"
        };
        db.AddRange(project, customer, conversation, followUp,
            new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = "old-incoming",
                Direction = "Incoming",
                Content = "مرحباً",
                MessageType = "Text",
                Timestamp = nowUtc.AddHours(-24)
            },
            new Message
            {
                ConversationId = conversation.Id,
                ExternalMessageId = "recent-outgoing",
                Direction = "Outgoing",
                Content = "أهلاً",
                MessageType = "Text",
                Timestamp = nowUtc.AddMinutes(-1)
            });
        await db.SaveChangesAsync();

        Assert.False(await FollowUpScheduler.HasMessengerReplyWindowAsync(db, followUp, nowUtc));

        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            ExternalMessageId = "recent-incoming",
            Direction = "Incoming",
            Content = "ممكن تفاصيل؟",
            MessageType = "Text",
            Timestamp = nowUtc.AddHours(-1)
        });
        await db.SaveChangesAsync();

        Assert.True(await FollowUpScheduler.HasMessengerReplyWindowAsync(db, followUp, nowUtc));
    }
}
