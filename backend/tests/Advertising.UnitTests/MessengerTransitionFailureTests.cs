using Modules.AI.Services;
using Modules.Projects.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MessengerTransitionFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saved_legacy_failure_does_not_blame_the_phone_for_a_connection_outage(bool channelOverride)
    {
        const string legacyFailure = "حاولنا نبعتلك على الواتساب بس غالباً الرقم غلط أو مش عليه واتساب. يا ريت تبعتلي الرقم الصح هنا عشان نتواصل هناك.";
        var service = new AIBehaviorSettingsService();
        var behavior = new AIBehaviorSettings();
        if (channelOverride)
            behavior.Channels["Messenger"] = new ChannelAIBehaviorSettings
            {
                Fallbacks = new FallbackMessageSettings { WhatsAppTransitionFailure = legacyFailure }
            };
        else
            behavior.Fallbacks.WhatsAppTransitionFailure = legacyFailure;

        var resolved = service.Resolve(new ProjectSettings
        {
            AiBehaviorSettingsJson = service.Serialize(behavior)
        }, "Messenger");

        Assert.DoesNotContain("الرقم غلط", resolved.Fallbacks.WhatsAppTransitionFailure);
        Assert.DoesNotContain("الرقم الصح", resolved.Fallbacks.WhatsAppTransitionFailure);
    }

    [Fact]
    public void Custom_failure_message_is_preserved()
    {
        const string customFailure = "برجاء التواصل مع خدمة العملاء لاستكمال المحادثة.";
        var service = new AIBehaviorSettingsService();
        var behavior = new AIBehaviorSettings
        {
            Fallbacks = new FallbackMessageSettings { WhatsAppTransitionFailure = customFailure }
        };

        var resolved = service.Resolve(new ProjectSettings
        {
            AiBehaviorSettingsJson = service.Serialize(behavior)
        }, "Messenger");

        Assert.Equal(customFailure, resolved.Fallbacks.WhatsAppTransitionFailure);
    }
}
