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
        db.AdvertisingConversionSourceEvents.Add(new ConversionSourceEvent { ProjectId = input.ProjectId, SourceSystem = input.Source, ExternalEventId = sourceId, PayloadHash = payloadHash, ReceivedAtUtc = DateTime.UtcNow });

        if (conversion is null)
        {
            conversion = new CanonicalConversion
            {
                ProjectId = input.ProjectId, CanonicalKey = canonicalKey, EventType = input.EventType,
                OccurredAtUtc = input.OccurredAtUtc, CustomerReference = input.CustomerReference,
                Value = input.Value, CurrentValue = input.Value, Currency = input.Currency,
                AttributionMethod = "InternalBusinessOutcome", ConsentState = ConsentState.NotRequired,
                State = input.IsCorrection ? ConversionState.Corrected : ConversionState.Verified
            };
            db.AdvertisingConversions.Add(conversion);
        }
        else if (input.IsCorrection)
        {
            var delta = -(conversion.CurrentValue ?? 0m);
            conversion.CurrentValue = 0m;
            conversion.State = ConversionState.Corrected;
            db.AdvertisingConversionAdjustments.Add(new ConversionAdjustment
            {
                ProjectId = input.ProjectId, ConversionId = conversion.Id, ExternalEventId = sourceId,
                Kind = "NegativeAdjustment", ValueDelta = delta, Reason = input.CorrectionReason, OccurredAtUtc = input.OccurredAtUtc
            });
        }
        else if (Rank(input.EventType) >= Rank(conversion.EventType))
        {
            conversion.EventType = input.EventType;
            conversion.OccurredAtUtc = input.OccurredAtUtc;
            conversion.Value ??= input.Value;
            conversion.CurrentValue = input.Value ?? conversion.CurrentValue;
            conversion.Currency ??= input.Currency;
            conversion.State = ConversionState.Verified;
        }
        await db.SaveChangesAsync(cancellationToken);
        return conversion.Id;
    }

    private static int Rank(string eventType) => Strength.TryGetValue(eventType, out var rank) ? rank : 0;
}
