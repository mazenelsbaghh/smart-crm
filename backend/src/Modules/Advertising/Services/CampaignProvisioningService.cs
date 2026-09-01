using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record CampaignProvisioningResult(Guid RootOperationId, Guid CampaignId, Guid AdSetId, Guid ProviderCreativeId,
    Guid AdvertisementId, string State);

public sealed class CampaignProvisioningService(
    AppDbContext db,
    MetaAdsClient meta,
    AdvertisingSecretVault vault,
    MetaProviderReconciliationService reconciliation,
    AdvertisingAuditService audit)
{
    public async Task<decimal?> ApplyInitialCanaryBudgetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var campaign = await db.AdvertisingManagedCampaigns.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.ExternalId != null && item.ConfiguredStatus == "PAUSED")
            .OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (campaign is null) return null;
        var plan = await db.AdvertisingCampaignPlans.IgnoreQueryFilters()
            .SingleAsync(item => item.ProjectId == projectId && item.Id == campaign.PlanId, cancellationToken);
        var canaryBudget = decimal.Round(Math.Min(plan.DailyBudget, 500m), 2);
        if (campaign.DailyBudget <= canaryBudget) return campaign.DailyBudget;
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleAsync(item =>
            item.ProjectId == projectId && item.Id == campaign.ConnectionId && item.ProtectedAccessToken != null, cancellationToken);
        try
        {
            await meta.SetDailyBudgetAsync(vault.Unprotect(connection.ProtectedAccessToken!), campaign.ExternalId!, canaryBudget, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.Message.Contains("1487566", StringComparison.Ordinal))
        {
            await MarkDeletedHierarchyAsync(campaign, cancellationToken);
            return null;
        }
        campaign.DailyBudget = plan.DailyBudget = canaryBudget;
        foreach (var adSet in await db.AdvertisingManagedAdSets.IgnoreQueryFilters().Where(item =>
            item.ProjectId == projectId && item.CampaignId == campaign.Id).ToListAsync(cancellationToken))
            adSet.DailyBudget = canaryBudget;
        foreach (var advertisement in await db.ManagedAdvertisements.IgnoreQueryFilters().Where(item =>
            item.ProjectId == projectId && item.PlanId == plan.Id).ToListAsync(cancellationToken))
            advertisement.DailyBudget = canaryBudget;
        await db.SaveChangesAsync(cancellationToken);
        return canaryBudget;
    }

    private async Task MarkDeletedHierarchyAsync(ManagedCampaign campaign, CancellationToken cancellationToken)
    {
        campaign.EffectiveStatus = "DELETED";
        var adSets = await db.AdvertisingManagedAdSets.IgnoreQueryFilters()
            .Where(item => item.ProjectId == campaign.ProjectId && item.CampaignId == campaign.Id)
            .ToListAsync(cancellationToken);
        foreach (var adSet in adSets) adSet.EffectiveStatus = "DELETED";
        var adSetIds = adSets.Select(item => item.Id).ToArray();
        var advertisements = await db.ManagedAdvertisements.IgnoreQueryFilters()
            .Where(item => item.ProjectId == campaign.ProjectId && item.AdSetId != null && adSetIds.Contains(item.AdSetId.Value))
            .ToListAsync(cancellationToken);
        foreach (var advertisement in advertisements) advertisement.EffectiveStatus = "DELETED";
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RetryFailedFinalAdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var operation = await db.AdvertisingProviderOperations.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.OperationType == "CreateAd"
                && item.State == ProviderOperationState.Failed && item.AttemptCount < 6)
            .OrderByDescending(item => item.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (operation?.LocalTargetId is not { } advertisementId) return false;
        var advertisement = await db.ManagedAdvertisements.IgnoreQueryFilters()
            .SingleAsync(item => item.ProjectId == projectId && item.Id == advertisementId, cancellationToken);
        var adSet = await db.AdvertisingManagedAdSets.IgnoreQueryFilters()
            .SingleAsync(item => item.ProjectId == projectId && item.Id == advertisement.AdSetId, cancellationToken);
        var creative = await db.AdvertisingManagedProviderCreatives.IgnoreQueryFilters()
            .SingleAsync(item => item.ProjectId == projectId && item.Id == advertisement.ManagedProviderCreativeId, cancellationToken);
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters()
            .SingleAsync(item => item.ProjectId == projectId && item.Id == advertisement.ConnectionId && item.ProtectedAccessToken != null, cancellationToken);
        if (adSet.ExternalId is null || creative.ExternalId is null || connection.AdAccountExternalId is null) return false;

        operation.State = ProviderOperationState.Sent;
        operation.AttemptCount++;
        operation.SentAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            if (operation.ErrorSummary?.Contains("1487891", StringComparison.Ordinal) == true)
            {
                var token = vault.Unprotect(connection.ProtectedAccessToken!);
                var source = await db.AdvertisingCreatives.IgnoreQueryFilters().SingleAsync(item =>
                    item.ProjectId == projectId && item.Id == creative.AdvertisingCreativeId, cancellationToken);
                var mediaUrl = (await meta.GetPageContentAsync(token, creative.PageExternalId, cancellationToken))
                    .FirstOrDefault(item => item.Id == source.SourceExternalId)?.MediaUrl;
                var replacement = new MetaCreativePayload(
                    $"{advertisement.Name} · WhatsApp compatible", creative.PageExternalId,
                    creative.WhatsAppPhoneExternalId, "WHATSAPP_MESSAGE", "WHATSAPP", "AiDerivative", string.Empty,
                    "راسلنا الآن على واتساب لمعرفة التفاصيل والحجز.", "اعرف التفاصيل على واتساب",
                    "سنرد عليك عبر واتساب لمساعدتك في اختيار الأنسب.", mediaUrl);
                creative.ExternalId = await meta.CreateProviderCreativeAsync(token,
                    connection.AdAccountExternalId, replacement, cancellationToken);
                creative.ProviderCreativeType = "ClickToWhatsApp";
                creative.VerificationState = ProviderCreativeVerificationState.Unverified;
                await db.SaveChangesAsync(cancellationToken);
            }
            else if (operation.ErrorSummary?.Contains("1487212", StringComparison.Ordinal) == true)
            {
                var token = vault.Unprotect(connection.ProtectedAccessToken!);
                var preferredVideo = await db.AdvertisingCreatives.IgnoreQueryFilters()
                    .Where(item => item.ProjectId == projectId && item.MediaType == CreativeMediaType.Video
                        && item.EligibilityState == CreativeEligibility.Eligible && item.SourceExternalId != null)
                    .OrderByDescending(item => item.RecommendationScore).ThenBy(item => item.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                var pageToken = await meta.GetPageAccessTokenAsync(token, creative.PageExternalId, cancellationToken);
                var pageContent = await meta.GetPageContentAsync(pageToken, creative.PageExternalId, cancellationToken);
                var videoPost = preferredVideo is null ? null : pageContent
                    .Where(item => item.MediaType == "Video" && item.MediaExternalId != null && item.MediaUrl != null)
                    .OrderByDescending(item => item.Id == preferredVideo.SourceExternalId || item.MediaExternalId == preferredVideo.SourceExternalId)
                    .ThenByDescending(item => item.CreatedAtUtc)
                    .FirstOrDefault();
                if (preferredVideo is not null && videoPost?.MediaExternalId is not null && videoPost.MediaUrl is not null)
                {
                    var videoAd = await meta.CreateWhatsAppVideoAdPausedAsync(token, new MetaWhatsAppVideoAdRequest(
                        connection.AdAccountExternalId, adSet.ExternalId, creative.PageExternalId,
                        creative.WhatsAppPhoneExternalId, videoPost.MediaExternalId, videoPost.MediaUrl,
                        $"{advertisement.Name} · Video", videoPost.Message ?? "راسلنا على واتساب لمعرفة التفاصيل."), cancellationToken);
                    creative.AdvertisingCreativeId = preferredVideo.Id;
                    creative.ExternalId = videoAd.CreativeId;
                    creative.ProviderCreativeType = "ClickToWhatsAppVideo";
                    advertisement.CreativeId = preferredVideo.Id;
                    advertisement.AdExternalId = videoAd.AdId;
                    advertisement.EffectiveStatus = "PAUSED";
                    advertisement.ReconciliationState = ProviderReconciliationState.PausedUnverified;
                    operation.ProviderTargetId = videoAd.AdId;
                    operation.State = ProviderOperationState.Succeeded;
                    operation.ErrorCode = operation.ErrorSummary = null;
                    operation.CompletedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync(cancellationToken);
                    return true;
                }
                throw new AdvertisingException("ADS_ELIGIBLE_VIDEO_REQUIRED",
                    "Meta could not reuse an eligible video; image fallback is disabled for this project.", 422);
            }
            advertisement.AdExternalId = await meta.CreateAdPausedAsync(vault.Unprotect(connection.ProtectedAccessToken!),
                connection.AdAccountExternalId, adSet.ExternalId, creative.ExternalId, advertisement.Name, cancellationToken);
            advertisement.EffectiveStatus = "PAUSED";
            advertisement.ReconciliationState = ProviderReconciliationState.PausedUnverified;
            operation.ProviderTargetId = advertisement.AdExternalId;
            operation.State = ProviderOperationState.Succeeded;
            operation.ErrorCode = operation.ErrorSummary = null;
            operation.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (HttpRequestException exception)
        {
            operation.State = ProviderOperationState.Failed;
            operation.ErrorCode = exception.StatusCode is null ? "META_REQUEST_FAILED" : $"META_{(int)exception.StatusCode.Value}";
            operation.ErrorSummary = SafeProviderError(exception.Message);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    public async Task<CampaignProvisioningResult> ProvisionPausedAsync(Guid projectId, Guid planId, Guid creativeId,
        Guid? variantId, Guid actorUserId, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        var prior = await db.AdvertisingProviderOperations.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.IdempotencyKey == idempotencyKey && x.State == ProviderOperationState.Succeeded, cancellationToken);
        if (prior?.LocalTargetId is { } previousCampaignId)
        {
            var previousCampaign = await db.AdvertisingManagedCampaigns.AsNoTracking().SingleAsync(x => x.ProjectId == projectId && x.Id == previousCampaignId, cancellationToken);
            var adSet = await db.AdvertisingManagedAdSets.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.CampaignId == previousCampaign.Id, cancellationToken);
            if (adSet is null)
                throw new AdvertisingException("ADS_PROVIDER_RECONCILIATION_REQUIRED", "The prior provider operation is incomplete and must be reconciled before retry.", 409);
            var ad = await db.ManagedAdvertisements.AsNoTracking().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.AdSetId == adSet.Id, cancellationToken);
            if (ad?.ManagedProviderCreativeId is null)
                throw new AdvertisingException("ADS_PROVIDER_RECONCILIATION_REQUIRED", "The prior provider hierarchy is incomplete and must be reconciled before retry.", 409);
            return new(prior.Id, previousCampaign.Id, adSet.Id, ad.ManagedProviderCreativeId!.Value, ad.Id, ad.ReconciliationState.ToString());
        }

        var plan = await db.AdvertisingCampaignPlans.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == planId && x.State == "Ready", cancellationToken)
            ?? throw new AdvertisingException("ADS_PLAN_NOT_READY", "Campaign plan is not ready for paused provisioning.", 409);
        var audience = await db.AdvertisingAudienceStrategies.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == plan.AudienceStrategyId, cancellationToken);
        var destination = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == plan.DestinationId, cancellationToken);
        var creative = await db.AdvertisingCreatives.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == creativeId, cancellationToken);
        if (creative.MediaType != CreativeMediaType.Video)
            throw new AdvertisingException("ADS_ELIGIBLE_VIDEO_REQUIRED", "This project only authorizes video advertisements.", 422);
        var variant = variantId is null ? null : await db.AdvertisingCreativeVariants.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == variantId, cancellationToken);
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == plan.ConnectionId && x.ProtectedAccessToken != null, cancellationToken);
        var capability = await db.AdvertisingCapabilitySnapshots.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == plan.CapabilitySnapshotId, cancellationToken);
        var capabilityDecision = AdvertisingCapabilityPolicy.CanProvisionWhatsApp(capability, DateTime.UtcNow);
        if (!capabilityDecision.Ready && destination.CapabilitySnapshotId is { } currentSnapshotId
            && currentSnapshotId != capability.Id)
        {
            var currentCapability = await db.AdvertisingCapabilitySnapshots.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.ProjectId == projectId && x.Id == currentSnapshotId && x.DestinationId == destination.Id, cancellationToken);
            if (currentCapability is not null)
            {
                var currentDecision = AdvertisingCapabilityPolicy.CanProvisionWhatsApp(currentCapability, DateTime.UtcNow);
                if (currentDecision.Ready)
                {
                    capability = currentCapability;
                    capabilityDecision = currentDecision;
                    plan.CapabilitySnapshotId = currentCapability.Id;
                }
            }
        }
        if (!capabilityDecision.Ready) throw new AdvertisingException(capabilityDecision.Code, "Current runtime capability is not ready.", 409);
        var payload = MetaCampaignPlanMapper.Map(plan, audience, destination, creative, variant);
        var token = vault.Unprotect(connection.ProtectedAccessToken!);

        var ownership = new ManagedOwnershipRecord
        {
            ProjectId = projectId, ConnectionId = connection.Id, OwnershipKind = ManagedOwnershipKind.AutopilotCreated,
            AuthorizedByUserId = actorUserId, AuthorizedAtUtc = DateTime.UtcNow,
            AllowedMutationScopeJson = "[\"Pause\",\"Resume\",\"Budget\",\"CreativeReplacement\"]"
        };
        var campaign = new ManagedCampaign
        {
            ProjectId = projectId, PlanId = plan.Id, ConnectionId = connection.Id, OwnershipRecordId = ownership.Id,
            Name = payload.Campaign.Name, ConfiguredStatus = "PAUSED", ReconciliationState = ProviderReconciliationState.Creating,
            Objective = payload.Campaign.Objective, BidStrategy = payload.Campaign.BidStrategy, DailyBudget = payload.Campaign.DailyBudget
        };
        ownership.RootManagedCampaignId = campaign.Id;
        db.AdvertisingManagedOwnership.Add(ownership);
        db.AdvertisingManagedCampaigns.Add(campaign);
        var root = Operation(projectId, connection.Id, plan.Id, null, "CreateCampaign", "Campaign", campaign.Id,
            idempotencyKey, payload.Campaign);
        db.AdvertisingProviderOperations.Add(root);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            root.State = ProviderOperationState.Sent; root.SentAtUtc = DateTime.UtcNow; root.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            campaign.ExternalId = await meta.CreateCampaignPausedAsync(token, connection.AdAccountExternalId!, payload.Campaign, cancellationToken);
            ownership.ProviderCampaignExternalId = campaign.ExternalId;
            root.ProviderTargetId = campaign.ExternalId; root.State = ProviderOperationState.Succeeded; root.CompletedAtUtc = DateTime.UtcNow;
            campaign.ReconciliationState = ProviderReconciliationState.PausedUnverified;
            await db.SaveChangesAsync(cancellationToken);

            var validate = Operation(projectId, connection.Id, plan.Id, root.Id, "ValidateAdSet", "AdSet", null,
                $"{idempotencyKey}:validate-adset", payload.AdSet);
            db.AdvertisingProviderOperations.Add(validate); await db.SaveChangesAsync(cancellationToken);
            if (SupportsValidateOnly(capability))
            {
                validate.State = ProviderOperationState.Sent; validate.SentAtUtc = DateTime.UtcNow; validate.AttemptCount++;
                await db.SaveChangesAsync(cancellationToken);
                var validation = await meta.ValidateAdSetAsync(token, connection.AdAccountExternalId!, campaign.ExternalId, payload.AdSet, cancellationToken);
                validate.ProviderTraceId = validation.ProviderTraceId; validate.ResponseFingerprint = AdvertisingAuditService.HashState(validation.EvidenceJson);
                validate.State = ProviderOperationState.Succeeded; validate.CompletedAtUtc = DateTime.UtcNow;
            }
            else
            {
                validate.State = ProviderOperationState.Succeeded;
                validate.ErrorCode = "ADS_VALIDATE_ONLY_UNSUPPORTED";
                validate.ErrorSummary = "Runtime capability did not expose validate_only; paused create plus full read-back remains mandatory.";
                validate.CompletedAtUtc = DateTime.UtcNow;
                db.AdvertisingProviderValidationFindings.Add(new ProviderValidationFinding
                {
                    ProjectId = projectId, PlanId = plan.Id, OperationId = validate.Id, Severity = InvariantSeverity.Warning,
                    Stage = "Preflight", ObjectType = "AdSet", Field = "validate_only", Code = "ADS_VALIDATE_ONLY_UNSUPPORTED",
                    Message = validate.ErrorSummary, NextSafeAction = "CreatePausedThenReadBack"
                });
            }

            var adSet = new ManagedAdSet
            {
                ProjectId = projectId, PlanId = plan.Id, ConnectionId = connection.Id, OwnershipRecordId = ownership.Id,
                CampaignId = campaign.Id, AudienceStrategyId = audience.Id, Name = payload.AdSet.Name,
                ConfiguredStatus = "PAUSED", ReconciliationState = ProviderReconciliationState.Creating,
                OptimizationGoal = payload.AdSet.OptimizationGoal, DestinationType = "WHATSAPP",
                PromotedPageExternalId = destination.PageExternalId, PromotedWhatsAppPhoneExternalId = destination.PhoneNumberExternalId,
                PlacementMode = PlacementPolicy.DynamicEligibleMeta
            };
            db.AdvertisingManagedAdSets.Add(adSet);
            var adSetOp = Operation(projectId, connection.Id, plan.Id, validate.Id, "CreateAdSet", "AdSet", adSet.Id,
                $"{idempotencyKey}:adset", payload.AdSet);
            db.AdvertisingProviderOperations.Add(adSetOp); await db.SaveChangesAsync(cancellationToken);
            adSetOp.State = ProviderOperationState.Sent; adSetOp.SentAtUtc = DateTime.UtcNow; adSetOp.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            adSet.ExternalId = await meta.CreateAdSetPausedAsync(token, connection.AdAccountExternalId!, campaign.ExternalId, payload.AdSet, cancellationToken);
            adSetOp.ProviderTargetId = adSet.ExternalId; adSetOp.State = ProviderOperationState.Succeeded; adSetOp.CompletedAtUtc = DateTime.UtcNow;
            adSet.ReconciliationState = ProviderReconciliationState.PausedUnverified;
            await db.SaveChangesAsync(cancellationToken);

            var providerCreative = new ManagedProviderCreative
            {
                ProjectId = projectId, PlanId = plan.Id, ConnectionId = connection.Id, OwnershipRecordId = ownership.Id,
                AdvertisingCreativeId = creative.Id, CreativeVariantId = variant?.Id ?? Guid.Empty, Name = payload.Creative.Name,
                SourceType = payload.Creative.SourceType, PageExternalId = destination.PageExternalId,
                WhatsAppPhoneExternalId = destination.PhoneNumberExternalId, CallToAction = "WHATSAPP_MESSAGE"
            };
            db.AdvertisingManagedProviderCreatives.Add(providerCreative);
            var creativeOp = Operation(projectId, connection.Id, plan.Id, adSetOp.Id, "CreateCreative", "Creative", providerCreative.Id,
                $"{idempotencyKey}:creative", payload.Creative);
            db.AdvertisingProviderOperations.Add(creativeOp); await db.SaveChangesAsync(cancellationToken);
            creativeOp.State = ProviderOperationState.Sent; creativeOp.SentAtUtc = DateTime.UtcNow; creativeOp.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            providerCreative.ExternalId = await meta.CreateProviderCreativeAsync(token, connection.AdAccountExternalId!, payload.Creative, cancellationToken);
            creativeOp.ProviderTargetId = providerCreative.ExternalId; creativeOp.State = ProviderOperationState.Succeeded; creativeOp.CompletedAtUtc = DateTime.UtcNow;
            providerCreative.VerificationState = ProviderCreativeVerificationState.Unverified;

            var advertisement = new ManagedAdvertisement
            {
                ProjectId = projectId, PlanId = plan.Id, ConnectionId = connection.Id, OwnershipRecordId = ownership.Id,
                AdSetId = adSet.Id, ManagedProviderCreativeId = providerCreative.Id, PromotionId = Guid.Empty,
                CreativeId = creative.Id, Name = $"{plan.Name} · Ad", CampaignExternalId = campaign.ExternalId,
                AdSetExternalId = adSet.ExternalId, DailyBudget = plan.DailyBudget, ConfiguredStatus = ManagedDeliveryState.Paused,
                EffectiveStatus = "UNKNOWN", ReconciliationState = ProviderReconciliationState.Creating,
                DestinationType = "WHATSAPP", DestinationId = destination.Id, ManagementSource = "CreatedBySystem",
                PublisherPlatform = "Meta", PositionsJson = "[]"
            };
            db.ManagedAdvertisements.Add(advertisement);
            var adOp = Operation(projectId, connection.Id, plan.Id, creativeOp.Id, "CreateAd", "Ad", advertisement.Id,
                $"{idempotencyKey}:ad", new
                {
                    advertisement.Name,
                    status = "PAUSED",
                    adSetExternalId = adSet.ExternalId,
                    providerCreativeExternalId = providerCreative.ExternalId
                });
            db.AdvertisingProviderOperations.Add(adOp); await db.SaveChangesAsync(cancellationToken);
            adOp.State = ProviderOperationState.Sent; adOp.SentAtUtc = DateTime.UtcNow; adOp.AttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            advertisement.AdExternalId = await meta.CreateAdPausedAsync(token, connection.AdAccountExternalId!, adSet.ExternalId!, providerCreative.ExternalId!, advertisement.Name, cancellationToken);
            adOp.ProviderTargetId = advertisement.AdExternalId; adOp.State = ProviderOperationState.Succeeded; adOp.CompletedAtUtc = DateTime.UtcNow;

            var findings = await reconciliation.VerifyHierarchyAsync(projectId, plan.Id, adSetOp.Id, payload,
                campaign.ExternalId!, adSet.ExternalId!, providerCreative.ExternalId!, advertisement.AdExternalId!, cancellationToken);
            if (findings.Any(finding => finding.Severity == InvariantSeverity.Blocking))
            {
                campaign.ReconciliationState = adSet.ReconciliationState = advertisement.ReconciliationState = ProviderReconciliationState.Drifted;
                plan.State = "Blocked";
                providerCreative.VerificationState = ProviderCreativeVerificationState.Drifted;
            }
            else
            {
                campaign.ReconciliationState = adSet.ReconciliationState = advertisement.ReconciliationState = ProviderReconciliationState.VerifiedPaused;
                campaign.EffectiveStatus = adSet.EffectiveStatus = advertisement.EffectiveStatus = "PAUSED";
                plan.State = "ProvisionedPaused";
                providerCreative.VerificationState = ProviderCreativeVerificationState.Verified;
            }
            audit.Append(new(projectId, "Provisioning", "PausedHierarchyProvisioned", nameof(CampaignPlan), plan.Id.ToString(),
                "User", actorUserId, JsonSerializer.Serialize(new
                {
                    campaignExternalId = campaign.ExternalId,
                    adSetExternalId = adSet.ExternalId,
                    providerCreativeExternalId = providerCreative.ExternalId,
                    advertisement.AdExternalId,
                    plan.State
                }), root.Id));
            await db.SaveChangesAsync(cancellationToken);
            return new(root.Id, campaign.Id, adSet.Id, providerCreative.Id, advertisement.Id, advertisement.ReconciliationState.ToString());
        }
        catch (HttpRequestException ex)
        {
            var sent = await db.AdvertisingProviderOperations.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.PlanId == plan.Id && x.State == ProviderOperationState.Sent).ToListAsync(cancellationToken);
            if (ex.StatusCode is >= System.Net.HttpStatusCode.BadRequest and < System.Net.HttpStatusCode.InternalServerError
                && ex.StatusCode is not System.Net.HttpStatusCode.RequestTimeout and not System.Net.HttpStatusCode.TooManyRequests)
            {
                foreach (var operation in sent)
                {
                    operation.State = ProviderOperationState.Failed;
                    operation.ErrorCode = $"META_{(int)ex.StatusCode.Value}";
                    operation.ErrorSummary = "Meta rejected the request; hierarchy remains paused.";
                }
                campaign.ReconciliationState = ProviderReconciliationState.Rejected;
                plan.State = "Blocked";
                await db.SaveChangesAsync(cancellationToken);
                throw new AdvertisingException("ADS_PROVIDER_REJECTED", "Meta rejected the paused hierarchy request.", 422);
            }
            foreach (var operation in sent) { operation.State = ProviderOperationState.Unknown; operation.ErrorCode = ex.StatusCode?.ToString() ?? "META_RESULT_UNKNOWN"; }
            campaign.ReconciliationState = ProviderReconciliationState.Unknown;
            plan.State = "ReconciliationRequired";
            await db.SaveChangesAsync(cancellationToken);
            throw new AdvertisingException("ADS_PROVIDER_RESULT_UNKNOWN", "Meta result is unknown; blind retry is blocked pending reconciliation.", 409);
        }
    }

    private static ProviderOperation Operation(Guid projectId, Guid connectionId, Guid planId, Guid? dependsOn,
        string operationType, string targetType, Guid? localTargetId, string idempotencyKey, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new ProviderOperation
        {
            ProjectId = projectId, ConnectionId = connectionId, PlanId = planId, DependsOnOperationId = dependsOn,
            OperationType = operationType, TargetType = targetType, LocalTargetId = localTargetId,
            IdempotencyKey = idempotencyKey, PlannedPayloadJson = json, RequestFingerprint = AdvertisingAuditService.HashState(json)
        };
    }

    private static string SafeProviderError(string message) => message.Length <= 1_500 ? message : message[..1_500];

    private static bool SupportsValidateOnly(AdvertisingCapabilitySnapshot capability) =>
        capability.SupportedValidationObjectsJson.Contains("AdSet", StringComparison.OrdinalIgnoreCase) ||
        capability.ValidationSupportJson.Contains("validate_only", StringComparison.OrdinalIgnoreCase);
}
