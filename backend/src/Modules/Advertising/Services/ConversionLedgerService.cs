using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record InternalConversionInput(Guid EventId, Guid ProjectId, string Source, string ExternalId, string EventType,
    DateTime OccurredAtUtc, string? CustomerReference, decimal? Value, string? Currency, bool IsCorrection = false, string? CorrectionReason = null);

public sealed class ConversionLedgerService(AppDbContext db)
{
    private static readonly IReadOnlyDictionary<string, int> Strength = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Lead"] = 10, ["QualifiedLead"] = 20, ["Signup"] = 30, ["TrialStarted"] = 40,
        ["BookingConfirmed"] = 50, ["EnrollmentPaid"] = 60, ["AttendanceConfirmed"] = 70,
        ["Purchase"] = 80, ["SubscriptionStarted"] = 80, ["SubscriptionRenewed"] = 90, ["DealWon"] = 80
    };

    public async Task<Guid> RecordAsync(InternalConversionInput input, CancellationToken cancellationToken = default)
    {
        var sourceId = input.EventId.ToString("N");
        if (await db.AdvertisingConversionSourceEvents.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == input.ProjectId && x.SourceSystem == input.Source && x.ExternalEventId == sourceId, cancellationToken))
            return (await db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == input.ProjectId && x.CanonicalKey == $"{input.Source}:{input.ExternalId}", cancellationToken)).Id;

        var canonicalKey = $"{input.Source}:{input.ExternalId}";
        var conversion = await db.AdvertisingConversions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == input.ProjectId && x.CanonicalKey == canonicalKey, cancellationToken);
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input)))).ToLowerInvariant();
        db.AdvertisingConversionSourceEvents.Add(new ConversionSourceEvent
        {
            ProjectId = input.ProjectId, SourceSystem = input.Source, ExternalEventId = sourceId,
            PayloadHash = payloadHash, NormalizedPayloadJson = JsonSerializer.Serialize(input), EventType = input.EventType,
            BusinessAggregateType = input.Source, BusinessAggregateId = input.ExternalId,
            JourneyLocation = "FirstParty", OccurredAtUtc = input.OccurredAtUtc, Value = input.Value,
            Currency = input.Currency, ReceivedAtUtc = DateTime.UtcNow,
            ProcessingState = input.IsCorrection && conversion is null ? "PendingBase" : "Accepted"
        });

        if (conversion is null)
        {
            conversion = new CanonicalConversion
            {
                ProjectId = input.ProjectId, CanonicalKey = canonicalKey, EventType = input.IsCorrection ? "PendingBase" : input.EventType,
                OccurredAtUtc = input.OccurredAtUtc, CustomerReference = input.CustomerReference,
                Value = input.IsCorrection ? null : input.Value, CurrentValue = input.IsCorrection ? 0m : input.Value, Currency = input.Currency,
                AttributionMethod = "InternalBusinessOutcome", ConsentState = ConsentState.NotRequired,
                State = input.IsCorrection ? ConversionState.Observed : ConversionState.Verified,
                TruthState = input.IsCorrection ? "PendingBase" : "Verified",
                CorrectionState = input.IsCorrection ? CorrectionState.PendingBase : CorrectionState.None
            };
            db.AdvertisingConversions.Add(conversion);
            if (input.IsCorrection) AddAdjustment(conversion, input, sourceId);
        }
        else if (input.IsCorrection)
        {
            var adjustment = AddAdjustment(conversion, input, sourceId);
            var adjustments = await db.AdvertisingConversionAdjustments.IgnoreQueryFilters()
                .Where(item => item.ProjectId == input.ProjectId && item.ConversionId == conversion.Id).ToListAsync(cancellationToken);
            adjustments.Add(adjustment);
            Recompute(conversion, adjustments);
        }
        else if (Rank(input.EventType) >= Rank(conversion.EventType))
        {
            conversion.EventType = input.EventType;
            conversion.OccurredAtUtc = input.OccurredAtUtc;
            conversion.Value ??= input.Value;
            conversion.Value = input.Value ?? conversion.Value;
            conversion.CurrentValue = conversion.Value;
            conversion.Currency ??= input.Currency;
            conversion.State = ConversionState.Verified;
            conversion.TruthState = "Verified";
            var adjustments = await db.AdvertisingConversionAdjustments.IgnoreQueryFilters()
                .Where(item => item.ProjectId == input.ProjectId && item.ConversionId == conversion.Id).ToListAsync(cancellationToken);
            Recompute(conversion, adjustments);
        }
        conversion.SourceHistoryJson = AppendHistory(conversion.SourceHistoryJson, input, sourceId);
        await db.SaveChangesAsync(cancellationToken);
        return conversion.Id;
    }

    private ConversionAdjustment AddAdjustment(CanonicalConversion conversion, InternalConversionInput input, string sourceId)
    {
        var adjustment = new ConversionAdjustment
        {
            ProjectId = input.ProjectId, ConversionId = conversion.Id, ExternalEventId = sourceId,
            Kind = input.EventType, ValueDelta = input.Value is { } value ? -Math.Abs(value) : 0m,
            Reason = input.CorrectionReason ?? input.EventType, OccurredAtUtc = input.OccurredAtUtc
        };
        db.AdvertisingConversionAdjustments.Add(adjustment);
        return adjustment;
    }

    private static void Recompute(CanonicalConversion conversion, IReadOnlyCollection<ConversionAdjustment> persisted)
    {
        var tracked = persisted.ToList();
        if (conversion.Value is null)
        {
            conversion.CurrentValue = 0m; conversion.CorrectionState = CorrectionState.PendingBase;
            conversion.TruthState = "PendingBase"; return;
        }
        var fullReversal = tracked.Any(adjustment => adjustment.Kind is "Cancellation" or "Refund" or "Chargeback" or "Absent" or "Churn"
            && adjustment.ValueDelta == 0m);
        conversion.CurrentValue = fullReversal ? 0m : Math.Max(0m, conversion.Value.Value + tracked.Sum(adjustment => adjustment.ValueDelta));
        conversion.CorrectionState = tracked.Count > 0 ? CorrectionState.Corrected : CorrectionState.None;
        conversion.State = tracked.Count > 0 ? ConversionState.Corrected : ConversionState.Verified;
        conversion.TruthState = tracked.Count > 0 ? "Corrected" : "Verified";
    }

    private static string AppendHistory(string existing, InternalConversionInput input, string sourceId)
    {
        var history = JsonSerializer.Deserialize<List<object>>(existing) ?? [];
        history.Add(new { sourceEventId = sourceId, input.Source, input.EventType, input.OccurredAtUtc,
            input.Value, input.Currency, input.IsCorrection });
        return JsonSerializer.Serialize(history);
    }

    private static int Rank(string eventType) => Strength.TryGetValue(eventType, out var rank) ? rank : 0;
}
