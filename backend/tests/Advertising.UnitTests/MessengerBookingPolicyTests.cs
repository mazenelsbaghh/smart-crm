using Modules.AI.Services;
using Modules.Projects.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MessengerBookingPolicyTests
{
    [Theory]
    [InlineData("Messenger", true, false)]
    [InlineData("Messenger", false, true)]
    [InlineData("Messenger", null, false)]
    [InlineData("WhatsApp", true, true)]
    [InlineData("FacebookComment", true, true)]
    public void Saved_booking_route_controls_model_booking_suggestions_only_on_Messenger(
        string channel, bool? transitionEnabled, bool bookingAllowed)
    {
        var behaviorService = new AIBehaviorSettingsService();
        var projectSettings = new ProjectSettings
        {
            AiBehaviorSettingsJson = transitionEnabled.HasValue ? behaviorService.Serialize(new AIBehaviorSettings
            {
                MessengerWhatsAppTransitionEnabled = transitionEnabled.Value
            }) : "{}"
        };
        var settings = behaviorService.Resolve(projectSettings, channel);
        var groupId = Guid.NewGuid().ToString();
        var analysis = new MarketingAnalysisResult
        {
            SuggestedGroupBookingId = groupId,
            SuggestedGroupBookingPeople = [new() { Name = "أحمد", PhoneNumber = "201123456789", IsRequester = true }],
            SuggestedFollowUp = new() { Needed = true, Type = "AppointmentReminder" },
            ReplyContent = "تم الحجز"
        };

        MessengerBookingPolicy.Apply(settings, channel, analysis);

        Assert.Equal(bookingAllowed ? groupId : null, analysis.SuggestedGroupBookingId);
        Assert.Equal(bookingAllowed ? 1 : 0, analysis.SuggestedGroupBookingPeople.Length);
        if (bookingAllowed) Assert.Equal("تم الحجز", analysis.ReplyContent);
        else
        {
            Assert.NotEqual("تم الحجز", analysis.ReplyContent);
            Assert.Null(analysis.SuggestedFollowUp);
        }
    }
}
