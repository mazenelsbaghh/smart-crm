using Modules.TalkTips.Services;

namespace Modules.AI.Services;

internal static class AiGroupBookingReplyPolicy
{
    public static string Apply(
        string generatedReply,
        bool shouldOfferTrial,
        AiGroupBookingResult? bookingResult)
    {
        if (bookingResult?.CustomerReplyOverride is { } bookingReplyOverride)
        {
            return bookingReplyOverride;
        }

        if (shouldOfferTrial &&
            bookingResult is { Succeeded: true, Failure: AiGroupBookingFailure.None })
        {
            return TalkTipsTrialCtaInstructions.AfterSuccessfulBooking(
                generatedReply,
                bookingResult.DisplayedRemainingPlaces);
        }

        return generatedReply;
    }
}
