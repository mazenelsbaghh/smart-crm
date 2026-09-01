using Modules.Advertising.Domain;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaWhatsAppInvariantTests
{
    [Fact]
    public void Every_effective_identity_and_cta_must_still_open_the_authorized_whatsapp_number()
    {
        var destinationId = Guid.NewGuid();
        var planned = new WhatsAppDeliveryEvidence(destinationId, "page-1", "phone-1", "WHATSAPP", "WHATSAPP_MESSAGE", "WHATSAPP");
        Assert.True(AdvertisingInvariants.ValidateWhatsAppDestination(planned, planned).IsValid);
        var drift = planned with { PhoneExternalId = "other-phone" };
        Assert.Contains(AdvertisingInvariants.ValidateWhatsAppDestination(planned, drift).Violations,
            violation => violation.Code == "ADS_WHATSAPP_DESTINATION_DRIFT");
    }
}
