using Modules.AI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Conversations.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class CourseDiscoveryPricingPolicyTests
{
    [Fact]
    public async Task Discovery_counts_delivered_bot_messages_across_both_channels_for_only_this_customer_and_project()
    {
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var sourceTimestamp = DateTime.UtcNow;
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var db = new AppDbContext(options, tenant, services);
        var messenger = new Conversation { ProjectId = projectId, CustomerId = customerId, Channel = "Messenger" };
        var whatsapp = new Conversation { ProjectId = projectId, CustomerId = customerId, Channel = "WhatsApp" };
        var otherProject = new Conversation { ProjectId = Guid.NewGuid(), CustomerId = customerId, Channel = "Messenger" };
        var otherCustomer = new Conversation { ProjectId = projectId, CustomerId = Guid.NewGuid(), Channel = "Messenger" };
        db.AddRange(messenger, whatsapp, otherProject, otherCustomer);
        Message Delivered(Conversation conversation, string senderType = "AI", string direction = "Outgoing", string type = "Text", bool later = false) => new()
        {
            ConversationId = conversation.Id, ExternalMessageId = Guid.NewGuid().ToString(), Content = "رسالة", SenderType = senderType,
            Direction = direction, MessageType = type, Timestamp = sourceTimestamp.AddSeconds(later ? 1 : -1)
        };
        db.Messages.AddRange(Delivered(messenger), Delivered(whatsapp), Delivered(whatsapp, "System"),
            Delivered(messenger, "Human"), Delivered(messenger, direction: "Incoming"),
            Delivered(messenger, type: "Reaction"), Delivered(otherProject), Delivered(otherCustomer),
            Delivered(messenger, later: true));
        await db.SaveChangesAsync();

        Assert.Equal(3, await CourseDiscoveryPricingPolicy.DeliveredMessageCountAsync(db, projectId, customerId, sourceTimestamp));
        Assert.Equal(4, await CourseDiscoveryPricingPolicy.DeliveredMessageCountAsync(db, projectId, customerId, null));
    }

    [Theory]
    [InlineData(0, "ممكن تفاصيل الكورس العادي", false)]
    [InlineData(4, "ممكن تفاصيل المكثف", false)]
    [InlineData(4, "المعاد امتى", false)]
    [InlineData(4, "عايز احجز", false)]
    [InlineData(5, "ممكن تفاصيل المكثف", true)]
    [InlineData(0, "بكام المكثف؟", true)]
    [InlineData(4, "تفاصيل الكورس العادي و السعر", true)]
    [InlineData(0, "how much is the intensive course?", true)]
    [InlineData(0, "في تقسيط؟", true)]
    [InlineData(0, "عرفت السعر بس المعاد امتى", false)]
    [InlineData(0, "السعر مش مهم، عايز أعرف المميزات", false)]
    [InlineData(0, "عرفت السعر بس في تقسيط؟", true)]
    public void First_five_messages_omit_unsolicited_pricing_but_answer_explicit_price_requests(
        int deliveredMessages, string customerMessage, bool priceAllowed)
    {
        const string reply = "في تدريب عملي للمحادثة.\n\nسعر الكورس ٦٠٠٠ جنيه، ومتاح على أقساط.\n\nإيه هدف حضرتك من تعلم الإنجليزي؟\n- شاهي ✨";

        var filtered = CourseDiscoveryPricingPolicy.Apply(reply, customerMessage, deliveredMessages);

        Assert.Contains("تدريب عملي", filtered);
        Assert.Contains("هدف حضرتك", filtered);
        Assert.Contains("- شاهي ✨", filtered);
        if (priceAllowed) Assert.Equal(reply, filtered);
        else
        {
            Assert.DoesNotContain("٦٠٠٠", filtered);
            Assert.DoesNotContain("جنيه", filtered);
            Assert.DoesNotContain("أقساط", filtered);
        }
    }

    [Theory]
    [InlineData("عندنا سيشن مجانية يوم 10/10 الساعة 6 مساءً، والكورس المكثف مدته 75 يوم.")]
    [InlineData("بما إن هدف حضرتك الشغل، تدريب المحادثة والمقابلات هيفيدك في الاستعداد.")]
    public void Discovery_preserves_free_sessions_schedules_durations_and_goal_specific_benefits(string reply)
    {
        Assert.Equal(reply, CourseDiscoveryPricingPolicy.Apply(reply, "ممكن تفاصيل", 0));
    }

    [Fact]
    public void Removing_a_price_only_answer_does_not_leave_a_signature_only_bubble()
    {
        var filtered = CourseDiscoveryPricingPolicy.Apply("4500 جنيه\n\n- شاهي ✨", "ممكن تفاصيل العادي", 0);

        Assert.DoesNotContain("4500", filtered);
        Assert.Contains("هدف", filtered);
        Assert.NotEqual("- شاهي ✨", filtered);
    }
}
