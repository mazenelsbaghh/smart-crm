using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.Advertising.API;

[Route("api/projects/{projectId:guid}/ad-manager/conversion-sources")]
[Route("api/projects/{projectId:guid}/ad-manager/webhook-sources")]
public sealed class AdvertisingConversionSourcesController(IProjectAuthorizationService authorization, AppDbContext db,
    AdvertisingWebhookSourceService sources) : AdvertisingControllerBase(authorization)
{
    [HttpPost]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] CreateWebhookSourceRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var created = await sources.CreateAsync(projectId, request.SourceKey, request.AllowedEventTypes ?? [], cancellationToken);
        return Created($"/api/projects/{projectId}/ad-manager/conversion-sources/{created.Source.Id}",
            new { created.Source.Id, created.Source.SourceKey, signingSecret = created.SigningSecret, shownOnce = true, created.Source.Version });
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingWebhookSources.AsNoTracking().Where(item => item.ProjectId == projectId)
            .Select(item => new { item.Id, item.SourceKey, state = item.State.ToString(), item.Version,
                item.AllowedEventTypesJson, item.RotatedAtUtc, item.OverlapEndsAtUtc, item.RevokedAtUtc, item.LastUsedAtUtc })
            .ToListAsync(cancellationToken));
    }

    [HttpPost("{sourceId:guid}/rotate")]
    public async Task<IActionResult> Rotate(Guid projectId, Guid sourceId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var rotated = await sources.RotateAsync(projectId, sourceId, cancellationToken);
        return Ok(new { rotated.Source.Id, rotated.Source.SourceKey, signingSecret = rotated.SigningSecret,
            shownOnce = true, rotated.Source.Version, rotated.Source.OverlapEndsAtUtc });
    }

    [HttpPost("{sourceId:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid projectId, Guid sourceId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        await sources.RevokeAsync(projectId, sourceId, cancellationToken);
        return NoContent();
    }
}

public sealed record CreateWebhookSourceRequest(string SourceKey, string[]? AllowedEventTypes);

[ApiController]
[AllowAnonymous]
[Route("api/integrations/ad-manager/{projectId:guid}/conversions/{sourceKey}")]
public sealed class AdvertisingConversionWebhookController(ConversionIngressService ingress) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Ingest(Guid projectId, string sourceKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Request.Headers["Idempotency-Key"]))
            return BadRequest(new { code = "ADS_IDEMPOTENCY_KEY_REQUIRED" });
        if (!long.TryParse(Request.Headers["X-Ads-Timestamp"], out var timestamp)) return Unauthorized(new { code = "ADS_SIGNATURE_REQUIRED" });
        var signature = Request.Headers["X-Ads-Signature"].ToString();
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            var result = await ingress.IngestAsync(projectId, sourceKey, timestamp, signature, rawBody, cancellationToken);
            return Accepted(new { result.ConversionId, result.Duplicate });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(new { code = "ADS_INVALID_SIGNATURE" }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("conflicting", StringComparison.OrdinalIgnoreCase)) { return Conflict(new { code = "ADS_DUPLICATE_CONFLICT" }); }
        catch (InvalidOperationException ex) { return UnprocessableEntity(new { code = "ADS_INVALID_CONVERSION", message = ex.Message }); }
    }
}

[Route("api/projects/{projectId:guid}/ad-manager/conversions")]
[Route("api/projects/{projectId:guid}/ad-manager/outcomes")]
public sealed class AdvertisingConversionsController(IProjectAuthorizationService authorization, AppDbContext db,
    AdvertisingTrackingHealthService tracking, Modules.Advertising.Infrastructure.Facebook.MetaBusinessMessagingClient businessMessaging,
    AdvertisingSecretVault vault, IAdvertisingReferralProtector referrals) : AdvertisingControllerBase(authorization)
{
    [HttpGet("~/api/projects/{projectId:guid}/ad-manager/daily-reports")]
    public async Task<IActionResult> DailyReport(Guid projectId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var reportingContext = await db.AutonomyEnvelopes.AsNoTracking().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt)
            .Select(item => new { item.ReportingTimezoneIana, item.Currency })
            .FirstOrDefaultAsync(cancellationToken);
        var accountContext = await db.AdvertisingConnections.AsNoTracking()
            .Where(item => item.ProjectId == projectId)
            .Select(item => new { item.AccountTimezoneIana, item.AccountCurrency })
            .SingleOrDefaultAsync(cancellationToken);
        var timezoneId = !string.IsNullOrWhiteSpace(reportingContext?.ReportingTimezoneIana)
            ? reportingContext.ReportingTimezoneIana
            : accountContext?.AccountTimezoneIana;
        var currency = !string.IsNullOrWhiteSpace(reportingContext?.Currency)
            ? reportingContext.Currency
            : accountContext?.AccountCurrency;
        if (string.IsNullOrWhiteSpace(timezoneId))
            return Conflict(new { code = "ADS_REPORTING_TIMEZONE_UNKNOWN", message = "A validated reporting timezone is required." });
        if (string.IsNullOrWhiteSpace(currency))
            return Conflict(new { code = "ADS_REPORTING_CURRENCY_UNKNOWN", message = "A provider-verified account currency is required." });
        TimeZoneInfo timezone;
        try { timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId); }
        catch (TimeZoneNotFoundException)
        {
            return Conflict(new { code = "ADS_REPORTING_TIMEZONE_INVALID", message = "The configured reporting timezone is unavailable." });
        }
        catch (InvalidTimeZoneException)
        {
            return Conflict(new { code = "ADS_REPORTING_TIMEZONE_INVALID", message = "The configured reporting timezone is invalid." });
        }
        var reportDate = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone));
        var localStart = DateTime.SpecifyKind(reportDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var localEnd = localStart.AddDays(1);
        if (timezone.IsInvalidTime(localStart) || timezone.IsInvalidTime(localEnd))
            return Conflict(new { code = "ADS_REPORTING_WINDOW_INVALID", message = "The requested local reporting boundary does not exist in the configured timezone." });
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timezone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(localEnd, timezone);

        var advertisements = await db.ManagedAdvertisements.AsNoTracking().Where(item => item.ProjectId == projectId)
            .Select(item => new { item.Id, item.Name, item.AdExternalId, item.CreativeId }).ToListAsync(cancellationToken);
        var creativeIds = advertisements.Select(item => item.CreativeId).Distinct().ToArray();
        var sources = await db.AdvertisingCreatives.AsNoTracking().Where(item => item.ProjectId == projectId && creativeIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, item => new { sourceType = item.SourceType.ToString(), item.SourceExternalId, mediaType = item.MediaType.ToString() }, cancellationToken);
        var observations = await db.AdvertisingAttributionObservations.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.MessageOccurredAtUtc >= startUtc && item.MessageOccurredAtUtc < endUtc)
            .Select(item => new { item.ConversationId, item.CustomerId, item.ProviderAdExternalId, item.MessageOccurredAtUtc }).ToListAsync(cancellationToken);
        var conversationIds = observations.Select(item => item.ConversationId).Distinct().ToArray();
        var touches = await db.AdvertisingAttributionTouches.AsNoTracking().Where(item => item.ProjectId == projectId
            && item.ConversationId != null && conversationIds.Contains(item.ConversationId.Value) && item.TouchedAtUtc < endUtc)
            .OrderByDescending(item => item.TouchedAtUtc).Select(item => new { item.ConversationId, item.AdvertisementId, item.ProviderAdExternalId }).ToListAsync(cancellationToken);
        var outcomes = await db.AdvertisingConversions.AsNoTracking().Where(item => item.ProjectId == projectId
            && item.OccurredAtUtc >= startUtc && item.OccurredAtUtc < endUtc)
            .Select(item => new { item.Id, item.AdvertisementId, item.EventType, item.AttributionTouchId, item.CustomerReference }).ToListAsync(cancellationToken);
        var outcomeIds = outcomes.Select(item => item.Id).ToArray();
        var outcomeTouchIds = outcomes.Where(item => item.AttributionTouchId != null).Select(item => item.AttributionTouchId!.Value).ToArray();
        var outcomeTouches = await db.AdvertisingAttributionTouches.AsNoTracking().Where(item => item.ProjectId == projectId
            && (outcomeIds.Contains(item.ConversionId ?? Guid.Empty) || outcomeTouchIds.Contains(item.Id)))
            .Select(item => new { item.Id, item.ConversionId, item.ConversationId, item.AdvertisementId }).ToListAsync(cancellationToken);
        var insights = await db.AdvertisingInsights.AsNoTracking().Where(item => item.ProjectId == projectId && item.IsCurrent
            && item.IntervalEndUtc > startUtc && item.IntervalStartUtc < endUtc)
            .GroupBy(item => item.TargetId).Select(group => new { AdvertisementId = group.Key, Spend = group.Sum(item => item.Spend) })
            .ToDictionaryAsync(item => item.AdvertisementId, item => item.Spend, cancellationToken);

        var conversationAd = observations.GroupBy(item => item.ConversationId).ToDictionary(group => group.Key, group =>
            touches.FirstOrDefault(touch => touch.ConversationId == group.Key)?.AdvertisementId
            ?? advertisements.FirstOrDefault(ad => group.Select(item => item.ProviderAdExternalId).Contains(ad.AdExternalId))?.Id);
        var cohortConversationIds = conversationIds.ToHashSet();
        var linkedOutcomes = outcomes.Select(outcome =>
        {
            var touch = outcomeTouches.FirstOrDefault(item => item.Id == outcome.AttributionTouchId)
                ?? outcomeTouches.FirstOrDefault(item => item.ConversionId == outcome.Id);
            var customerConversationId = Guid.TryParse(outcome.CustomerReference, out var customerId)
                ? observations.Where(item => item.CustomerId == customerId)
                    .OrderByDescending(item => item.MessageOccurredAtUtc).Select(item => (Guid?)item.ConversationId).FirstOrDefault()
                : null;
            var conversationId = touch?.ConversationId ?? customerConversationId;
            return new { outcome.EventType, ConversationId = conversationId };
        }).ToArray();
        var cohortOutcomes = linkedOutcomes.Where(item => item.ConversationId != null
            && cohortConversationIds.Contains(item.ConversationId.Value)).ToArray();
        static bool IsBooking(string eventType) => eventType is "BookingConfirmed" or "EnrollmentPaid" or "AttendanceConfirmed";
        var rows = advertisements.Select(advertisement =>
        {
            var adConversationIds = conversationAd.Where(item => item.Value == advertisement.Id).Select(item => item.Key).ToHashSet();
            var entrants = adConversationIds.Count;
            var adOutcomes = cohortOutcomes.Where(item => item.ConversationId != null
                && adConversationIds.Contains(item.ConversationId.Value)).ToArray();
            var bookingConversationIds = adOutcomes.Where(item => IsBooking(item.EventType)).Select(item => item.ConversationId!.Value).Distinct().ToHashSet();
            var qualifiedConversationIds = adOutcomes.Where(item => item.EventType == "QualifiedLead").Select(item => item.ConversationId!.Value)
                .Concat(bookingConversationIds).Distinct().ToHashSet();
            var qualified = qualifiedConversationIds.Count;
            var bookings = bookingConversationIds.Count;
            sources.TryGetValue(advertisement.CreativeId, out var source);
            var spend = insights.GetValueOrDefault(advertisement.Id);
            return new { advertisement.Id, advertisement.Name, advertisement.AdExternalId, source, entrants, qualified, bookings, spend,
                costPerEntrant = entrants == 0 ? (decimal?)null : spend / entrants,
                costPerQualified = qualified == 0 ? (decimal?)null : spend / qualified,
                qualificationRate = entrants == 0 ? (decimal?)null : decimal.Round((decimal)qualified / entrants, 4),
                bookingRate = qualified == 0 ? (decimal?)null : decimal.Round((decimal)bookings / qualified, 4) };
        }).Where(row => row.entrants > 0 || row.qualified > 0 || row.bookings > 0 || row.spend > 0).ToArray();
        var attributedConversationIds = conversationAd.Where(item => item.Value != null).Select(item => item.Key).ToHashSet();
        var unattributedEntrants = conversationIds.Count(id => !attributedConversationIds.Contains(id));
        var unattributedOutcomes = linkedOutcomes.Where(item => item.ConversationId == null
            || !cohortConversationIds.Contains(item.ConversationId.Value)).ToArray();
        var totalBookingConversationIds = cohortOutcomes.Where(item => IsBooking(item.EventType))
            .Select(item => item.ConversationId!.Value).Distinct().ToHashSet();
        var totalQualifiedConversationIds = cohortOutcomes.Where(item => item.EventType == "QualifiedLead")
            .Select(item => item.ConversationId!.Value).Concat(totalBookingConversationIds).Distinct().ToHashSet();
        return Ok(new { date = reportDate.ToString("yyyy-MM-dd"), timezone = timezoneId, currency, startUtc, endUtc,
            totals = new { entrants = conversationIds.Length, qualified = totalQualifiedConversationIds.Count,
                bookings = totalBookingConversationIds.Count, spend = insights.Values.Sum() },
            rows, unattributed = new { entrants = unattributedEntrants,
                qualified = unattributedOutcomes.Count(item => item.EventType == "QualifiedLead"),
                bookings = unattributedOutcomes.Count(item => IsBooking(item.EventType)) } });
    }

    [HttpGet]
    public async Task<IActionResult> Truth(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingConversions.AsNoTracking().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.OccurredAtUtc).Take(200).Select(item => new { item.Id, item.CanonicalKey,
                item.EventType, item.OccurredAtUtc, item.CurrentValue, item.Currency, item.TruthState,
                attributionState = item.AttributionState.ToString(), correctionState = item.CorrectionState.ToString(),
                state = item.State.ToString(), item.AttributionMethod, item.AttributionTouchId }).ToListAsync(cancellationToken));
    }

    [HttpGet("touches")]
    public async Task<IActionResult> Touches(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingAttributionTouches.AsNoTracking().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.TouchedAtUtc).Take(200).Select(item => new { item.Id, item.ConversionId,
                item.ConversationId, item.DestinationId, item.AdvertisementId, item.Method,
                hasClickIdentifier = item.ProtectedCtwaClid != null, item.ProviderAdExternalId, item.TouchedAtUtc }).ToListAsync(cancellationToken));
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> Deliveries(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingConversionDeliveries.AsNoTracking().Where(item => item.ProjectId == projectId)
            .OrderByDescending(item => item.CreatedAt).Take(200).Select(item => new { item.Id, item.ConversionId,
                item.EventName, state = item.State.ToString(), item.AcceptedAtUtc, item.NextAttemptAtUtc,
                item.SuppressionReason }).ToListAsync(cancellationToken));
    }

    [HttpGet("business-messaging/readiness")]
    [HttpGet("~/api/projects/{projectId:guid}/ad-manager/business-messaging/readiness")]
    public async Task<IActionResult> BusinessMessagingReadiness(Guid projectId, [FromQuery] Guid destinationId,
        CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var destination = await db.AdvertisingWhatsAppDestinations.AsNoTracking().SingleOrDefaultAsync(item => item.ProjectId == projectId && item.Id == destinationId, cancellationToken);
        if (destination is null) return NotFound();
        var connection = await db.AdvertisingConnections.AsNoTracking().SingleAsync(item => item.ProjectId == projectId && item.Id == destination.ConnectionId, cancellationToken);
        var hasReferral = await db.AdvertisingAttributionTouches.AsNoTracking().AnyAsync(item => item.ProjectId == projectId
            && item.DestinationId == destinationId && item.ProtectedCtwaClid != null, cancellationToken);
        var reasons = new List<string>();
        if (string.IsNullOrWhiteSpace(destination.DatasetExternalId)) reasons.Add("ADS_DATASET_REQUIRED");
        if (string.IsNullOrWhiteSpace(destination.WabaExternalId)) reasons.Add("ADS_WABA_REQUIRED");
        if (!connection.GrantedPermissionsJson.Contains("whatsapp_business_manage_events", StringComparison.OrdinalIgnoreCase)) reasons.Add("ADS_BUSINESS_EVENTS_PERMISSION_REQUIRED");
        if (!hasReferral) reasons.Add("ADS_REAL_CTWA_CLID_REQUIRED");
        return Ok(new { ready = reasons.Count == 0, reasons, destination.ReferralCaptureState });
    }

    [HttpPost("business-messaging/test")]
    [HttpPost("~/api/projects/{projectId:guid}/ad-manager/business-messaging/test")]
    public async Task<IActionResult> BusinessMessagingTest(Guid projectId, [FromBody] BusinessMessagingTestRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var destination = await db.AdvertisingWhatsAppDestinations.SingleAsync(item => item.ProjectId == projectId && item.Id == request.DestinationId, cancellationToken);
        var connection = await db.AdvertisingConnections.SingleAsync(item => item.ProjectId == projectId && item.Id == destination.ConnectionId && item.ProtectedAccessToken != null, cancellationToken);
        var touch = await db.AdvertisingAttributionTouches.OrderByDescending(item => item.TouchedAtUtc).FirstOrDefaultAsync(item =>
            item.ProjectId == projectId && item.DestinationId == destination.Id && item.ProtectedCtwaClid != null, cancellationToken)
            ?? throw new AdvertisingException("ADS_REAL_CTWA_CLID_REQUIRED", "A real Cloud/coexistence click referral is required for the Dataset test event.", 409);
        var result = await businessMessaging.SendAsync(vault.Unprotect(connection.ProtectedAccessToken!), new(
            destination.DatasetExternalId, destination.WabaExternalId,
            referrals.UnprotectForBusinessMessaging(touch.ProtectedCtwaClid!), "QualifiedLead",
            $"test:{projectId:N}:{touch.Id:N}", touch.TouchedAtUtc, null, null, request.TestEventCode), cancellationToken);
        return Ok(new { accepted = result.EventsReceived > 0, result.EventsReceived, result.ProviderRequestId, result.ProviderTraceId });
    }

    [HttpPost("tracking/evaluate")]
    [HttpPost("~/api/projects/{projectId:guid}/ad-manager/tracking-health/evaluate")]
    public async Task<IActionResult> EvaluateTracking(Guid projectId, [FromBody] EvaluateTrackingRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanManage(projectId) && !IsAutopilot(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var snapshot = await tracking.EvaluateAsync(projectId, request.DestinationId, cancellationToken);
        return Ok(new { snapshot.Id, state = snapshot.State.ToString(), snapshot.TrackingHealthPolicyVersion,
            snapshot.ReferralCoverage, snapshot.ExactMatchRate, snapshot.ProviderMatchQuality,
            snapshot.DeliveryAcceptanceRate, snapshot.CorrectionRate, snapshot.EventDelayMinutesP95,
            snapshot.ReasonCodesJson, snapshot.EvaluatedAtUtc });
    }

    [HttpGet("~/api/projects/{projectId:guid}/ad-manager/tracking-health")]
    public async Task<IActionResult> TrackingHistory(Guid projectId, [FromQuery] DateTime? from,
        [FromQuery] DateTime? to, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var start = from?.ToUniversalTime() ?? DateTime.UtcNow.AddDays(-30);
        var end = to?.ToUniversalTime() ?? DateTime.UtcNow;
        if (start >= end) throw new AdvertisingException("ADS_REPORTING_WINDOW_INVALID", "Tracking window is invalid.", 422);
        var snapshots = await db.AdvertisingTrackingHealthSnapshots.AsNoTracking()
            .Where(item => item.ProjectId == projectId && item.EvaluatedAtUtc >= start && item.EvaluatedAtUtc < end)
            .OrderByDescending(item => item.EvaluatedAtUtc).Take(200)
            .Select(item => new { item.Id, item.DestinationId, state = item.State.ToString(),
                item.TrackingHealthPolicyVersion, item.InboundConversationCount, item.ValidReferralCount,
                item.ReferralCoverage, item.ExactMatchRate, item.ProviderMatchQuality, item.DeliveryAcceptanceRate,
                item.CorrectionRate, item.EventDelayMinutesP95, item.SourceFreshnessUtc,
                item.ReasonCodesJson, item.WindowStartUtc, item.WindowEndUtc, item.EvaluatedAtUtc })
            .ToListAsync(cancellationToken);
        return Ok(snapshots);
    }
}

public sealed record BusinessMessagingTestRequest(Guid DestinationId, string TestEventCode);
public sealed record EvaluateTrackingRequest(Guid DestinationId);
