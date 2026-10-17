using Modules.TalkTips.Services;

namespace Modules.AI.Services;

internal static class AiGroupBookingReplyPolicy
{
    public static string Apply(
        string generatedReply,
        bool shouldOfferTrial,
        AiGroupBookingResult? bookingResult,
        bool shouldPrepareBeforeSession = false)
    {
        if (bookingResult is { Succeeded: true, ConfirmedAppointmentDetails: { } details })
            generatedReply = $"تم تسجيل {string.Join("، ", bookingResult.BookedPeople)} في الحصة المجانية بنجاح.\n{details}";
        if (bookingResult?.CustomerReplyOverride is { } bookingReplyOverride)
        {
            if (bookingResult.Succeeded && bookingResult.ConfirmedAppointmentDetails is { } partialDetails)
                bookingReplyOverride += "\n" + partialDetails;
            if (shouldPrepareBeforeSession && bookingResult.Succeeded)
                return TalkTipsTrialCtaInstructions.BookingTrialLast(
                    $"{bookingReplyOverride}\n\n{Modules.CRM.Services.GroupSessionPreparationScheduler.Confirmation}");
            return bookingReplyOverride;
        }

        if (shouldPrepareBeforeSession && bookingResult is { Succeeded: true, Failure: AiGroupBookingFailure.None })
        {
            var confirmation = generatedReply;
            if (shouldOfferTrial)
                confirmation = TalkTipsTrialCtaInstructions.AfterSuccessfulBooking(confirmation, bookingResult.DisplayedRemainingPlaces, includeTrial: false);
            return TalkTipsTrialCtaInstructions.BookingTrialLast(
                $"{confirmation}\n\n{Modules.CRM.Services.GroupSessionPreparationScheduler.Confirmation}");
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
