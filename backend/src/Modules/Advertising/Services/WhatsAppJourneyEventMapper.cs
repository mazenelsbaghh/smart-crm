namespace Modules.Advertising.Services;

public sealed record JourneyEventMapping(string InternalEvent, string? MetaMessagingEvent, int TruthStrength, bool IsNegative);

public static class WhatsAppJourneyEventMapper
{
    public static JourneyEventMapping Map(string eventType) => eventType switch
    {
        "ConversationStarted" => new(eventType, null, 10, false),
        "QualifiedLead" => new(eventType, "QualifiedLead", 30, false),
        "ViewContent" => new(eventType, "ViewContent", 20, false),
        "AddToCart" => new(eventType, "AddToCart", 25, false),
        "InitiateCheckout" => new(eventType, "InitiateCheckout", 35, false),
        "OrderCreated" => new(eventType, "OrderCreated", 50, false),
        "Purchase" => new(eventType, "Purchase", 80, false),
        "OrderShipped" => new(eventType, "OrderShipped", 60, false),
        "OrderDelivered" => new(eventType, "OrderDelivered", 70, false),
        "Cancellation" => new(eventType, "OrderCanceled", 90, true),
        "Refund" => new(eventType, "OrderReturned", 100, true),
        _ => new(eventType, null, 0, false)
    };
}
