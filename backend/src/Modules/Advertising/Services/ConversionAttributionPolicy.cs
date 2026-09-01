namespace Modules.Advertising.Services;

public enum ConversionDeliveryChannel { MetaBusinessMessaging, WebConversionsApi, AppConversionsApi, InternalOnly }

public static class ConversionAttributionPolicy
{
    public static ConversionDeliveryChannel Route(string journeyLocation, bool hasCtwaClid) => journeyLocation switch
    {
        "MessagingThread" when hasCtwaClid => ConversionDeliveryChannel.MetaBusinessMessaging,
        "Website" => ConversionDeliveryChannel.WebConversionsApi,
        "App" => ConversionDeliveryChannel.AppConversionsApi,
        _ => ConversionDeliveryChannel.InternalOnly
    };
}
