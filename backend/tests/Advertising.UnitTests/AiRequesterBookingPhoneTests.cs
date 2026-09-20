using Modules.AI.Services;
using Modules.Conversations.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AiRequesterBookingPhoneTests
{
    [Theory]
    [InlineData("01123456789")]
    [InlineData("٠١١ ٢٣٤٥ ٦٧٨٩")]
    [InlineData("رقمي 01123456789")]
    public void Production_2026_09_08_phone_reply_completes_the_booking_even_when_the_model_omits_the_phone(string message)
    {
        var history = PhoneConversation(message);
        var analysis = Booking(null);
        analysis.SuggestedGroupBookingPeople = [];

        var phone = AiRequesterBookingPhone.FromConversation(history, message);
        AiRequesterBookingPhone.Apply(phone, analysis, [message]);

        Assert.NotNull(analysis.SuggestedGroupBookingId);
        Assert.Equal("201123456789", Assert.Single(analysis.SuggestedGroupBookingPeople).PhoneNumber);
        Assert.False(analysis.RequestHuman);
    }

    [Theory]
    [InlineData("علشان أتمم الحجز، ابعتي رقم موبايلك الأول لو سمحت لأن رقمك مش ظاهر عندي.")]
    [InlineData("ممكن تبعت رقم الموبايل؟")]
    [InlineData("Please send your phone number.")]
    public void A_repeated_request_for_a_supplied_phone_requires_real_handoff_instead_of_another_booking_attempt(string reply)
    {
        var analysis = Booking(null);
        analysis.ReplyContent = reply;
        var phone = AiRequesterBookingPhone.FromConversation(PhoneConversation("01123456789"), "01123456789");

        AiRequesterBookingPhone.Apply(phone, analysis, ["01123456789"]);

        Assert.True(analysis.RequestHuman);
        Assert.Null(analysis.SuggestedGroupBookingId);
        Assert.False(analysis.SuggestedFollowUp!.Needed);
    }

    [Theory]
    [InlineData("ممكن رقم أختك؟", "01123456789")]
    [InlineData("ممكن تبعت رقم موبايلك؟", "ده رقم أختي 01123456789")]
    [InlineData("ممكن تبعت رقم موبايلك؟", "99901123456789")]
    [InlineData("ممكن تبعت رقم موبايلك؟", "01123456789 و01011112222")]
    public void Other_people_or_ambiguous_numbers_are_not_assigned_to_the_requester(string question, string message)
    {
        var history = PhoneConversation(message);
        history[0].Content = question;

        Assert.Null(AiRequesterBookingPhone.FromConversation(history, message));
    }

    [Fact]
    public void A_previously_supplied_requester_phone_survives_reactions_and_later_companion_phone_collection()
    {
        var history = PhoneConversation("01123456789").ToList();
        history.Add(new() { Direction = "Outgoing", Content = "ممكن اسم ورقم أختك؟", Timestamp = DateTime.UtcNow.AddMinutes(-1) });
        history.Add(new() { Direction = "Incoming", Content = "01011112222", Timestamp = DateTime.UtcNow });
        var analysis = Booking(null);
        analysis.ReplyContent = "ممكن اسم ورقم أختك؟";

        var phone = AiRequesterBookingPhone.FromConversation(history, "01011112222");
        AiRequesterBookingPhone.Apply(phone, analysis, ["01123456789", "01011112222"]);

        Assert.Equal("201123456789", Assert.Single(analysis.SuggestedGroupBookingPeople).PhoneNumber);
        Assert.False(analysis.RequestHuman);
    }

    [Theory]
    [InlineData("رقمك وصل، ممكن اسمك بالكامل؟")]
    [InlineData("رقمك وصل ممكن تختار الميعاد المناسب؟")]
    public void Acknowledging_the_phone_and_asking_for_other_missing_details_does_not_pause_automation(string reply)
    {
        var analysis = new MarketingAnalysisResult { ReplyContent = reply };

        AiRequesterBookingPhone.Apply("201123456789", analysis, ["01123456789"]);

        Assert.False(analysis.RequestHuman);
        Assert.Equal(reply, analysis.ReplyContent);
    }

    [Theory]
    [InlineData("01123456789")]
    [InlineData("٠١١٢٣٤٥٦٧٨٩")]
    [InlineData("رقمي 011 2345 6789")]
    public void Production_2026_09_08_supplied_requester_phone_keeps_the_booking(string customerMessage)
    {
        var analysis = Booking("01123456789");

        AiRequesterBookingPhone.Apply(null, analysis, [customerMessage, "الاتنين والتلات الساعة ٨"]);

        Assert.NotNull(analysis.SuggestedGroupBookingId);
        Assert.Equal("201123456789", Assert.Single(analysis.SuggestedGroupBookingPeople).PhoneNumber);
        Assert.Equal("تمام، اختيارك وصل", analysis.ReplyContent);
    }

    [Fact]
    public void A_remembered_booking_phone_is_used_without_changing_the_customer_identity()
    {
        var analysis = Booking(null);
        analysis.SuggestedGroupBookingPeople = [];

        AiRequesterBookingPhone.Apply("201123456789", analysis, ["عايز أنقل الحجز"]);

        Assert.NotNull(analysis.SuggestedGroupBookingId);
        Assert.Equal("201123456789", Assert.Single(analysis.SuggestedGroupBookingPeople).PhoneNumber);
    }

    [Theory]
    [InlineData("201123456789", "ممكن أحجز؟")]
    [InlineData("person@lid", "person@lid")]
    [InlineData(null, "الاتنين الساعة ٨")]
    public void Missing_or_unsubstantiated_requester_phone_does_not_confirm_a_booking(string? suggestion, string message)
    {
        var analysis = Booking(suggestion);

        AiRequesterBookingPhone.Apply(null, analysis, [message]);

        Assert.Null(analysis.SuggestedGroupBookingId);
        Assert.False(analysis.SuggestedFollowUp!.Needed);
    }

    [Fact]
    public void Booking_only_for_someone_else_does_not_request_the_senders_phone()
    {
        var analysis = Booking("201123456789");
        analysis.SuggestedGroupBookingPeople[0].IsRequester = false;

        AiRequesterBookingPhone.Apply(null, analysis, ["احجز لأختي"]);

        Assert.NotNull(analysis.SuggestedGroupBookingId);
        Assert.Equal("تمام، اختيارك وصل", analysis.ReplyContent);
    }

    private static MarketingAnalysisResult Booking(string? phone) => new()
    {
        SuggestedGroupBookingId = Guid.NewGuid().ToString(),
        SuggestedGroupBookingPeople = [new() { IsRequester = true, PhoneNumber = phone }],
        ReplyContent = "تمام، اختيارك وصل",
        SuggestedFollowUp = new() { Needed = true, Type = "AppointmentReminder" }
    };

    private static Message[] PhoneConversation(string phone) =>
    [
        new() { Direction = "Outgoing", Content = "علشان أتمم الحجز، ابعتي رقم موبايلك لأن رقمك مش ظاهر عندي.", Timestamp = DateTime.UtcNow.AddMinutes(-4) },
        new() { Direction = "Incoming", Content = phone, Timestamp = DateTime.UtcNow.AddMinutes(-3) },
        new() { Direction = "Outgoing", MessageType = "Reaction", Content = "❤️", Timestamp = DateTime.UtcNow.AddMinutes(-2) }
    ];
}
