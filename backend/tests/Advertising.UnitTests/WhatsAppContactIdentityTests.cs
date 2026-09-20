using Modules.Conversations.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class WhatsAppContactIdentityTests
{
    [Theory]
    [InlineData("رقم التحويل 01012345678", "201099999999")]
    [InlineData("رقم أحمد 01012345678", "201099999999")]
    [InlineData("اسمي أحمد علي 01012345678", "123456789123456@lid")]
    public void Mentioning_a_phone_is_not_proof_of_sender_ownership(string text, string sender)
        => Assert.Null(WhatsAppSharedContactParser.ExtractVerifiedSenderContact(text, sender));

    [Fact]
    public void Contact_name_can_be_read_when_phone_matches_the_provider_verified_sender()
    {
        var contact = WhatsAppSharedContactParser.ExtractVerifiedSenderContact("أحمد علي\n01012345678", "201012345678");
        Assert.NotNull(contact);
        Assert.Equal("أحمد علي", contact.Name);
        Assert.Equal("201012345678", contact.PhoneNumber);
    }
}
