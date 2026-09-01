using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Services;

public sealed record ReadinessItem(string Key, string Label, bool Ready, string? Reason = null);
public sealed record AdvertisingReadiness(bool Ready, IReadOnlyList<ReadinessItem> Items);
public sealed record AuthorizeWhatsAppDestinationRequest(string AdAccountId, string PageId, string WabaId, string PhoneNumberId,
    string DatasetId, WhatsAppIntegrationMode IntegrationMode, Guid? WhatsAppAccountId = null);
public sealed record AuthorizedDestinationResult(Guid ConnectionId, Guid DestinationId, Guid CapabilitySnapshotId, string State);
public sealed record AdvertisingReadinessOverviewState(
    AdvertisingConnection? Connection,
    AutonomyEnvelope? ActiveEnvelope,
    TrackingHealthSnapshot? LatestTracking,
    bool HasTrackingIncident);

public sealed class AdvertisingReadinessService(
    AppDbContext db,
    MetaCapabilityClient capabilities,
    WhatsAppGatewaySessionClient gateway,
    AdvertisingSecretVault vault,
    AdvertisingAuditService audit,
    WhatsAppAccountService whatsAppAccounts)
{
    private static readonly string[] RequiredMetaPermissions =
        ["ads_read", "ads_management", "business_management", "pages_show_list"];
    private static readonly string[] RequiredCloudPermissions =
        ["whatsapp_business_management", "whatsapp_business_manage_events"];

    public async Task<MetaCapabilityCatalog> DiscoverAsync(Guid projectId, string? adAccountId, CancellationToken cancellationToken = default)
    {
        var connection = await ConnectionWithToken(projectId, cancellationToken);
        return await capabilities.DiscoverAsync(vault.Unprotect(connection.ProtectedAccessToken!), adAccountId, cancellationToken);
    }

    public async Task<AuthorizedDestinationResult> AuthorizeDestinationAsync(Guid projectId,
        AuthorizeWhatsAppDestinationRequest request, CancellationToken cancellationToken = default)
    {
        var connection = await ConnectionWithToken(projectId, cancellationToken);
        var token = vault.Unprotect(connection.ProtectedAccessToken!);
        var catalog = await capabilities.DiscoverAsync(token, request.AdAccountId, cancellationToken);
        var account = catalog.AdAccounts.SingleOrDefault(x => x.Id == request.AdAccountId);
        var page = catalog.Pages.SingleOrDefault(x => x.Id == request.PageId);
        var gatewayMode = request.IntegrationMode == WhatsAppIntegrationMode.BaileysObservedExperimental;
        var requiredPermissions = gatewayMode ? RequiredMetaPermissions : RequiredMetaPermissions.Concat(RequiredCloudPermissions);
        var missingPermission = requiredPermissions.FirstOrDefault(required => !catalog.GrantedPermissions.Contains(required, StringComparer.Ordinal));
        if (account is null || page is null || missingPermission is not null)
            throw new AdvertisingException("ADS_META_RESOURCES_NOT_ELIGIBLE", "The selected ad account and Page are not accessible with the current Meta authorization.");
        if (account.Status is not 1)
            throw new AdvertisingException("ADS_ACCOUNT_INACTIVE", "The selected ad account is not active.");

        MetaWaba? waba = null;
        MetaWhatsAppPhone? phone = null;
        MetaResource? dataset = null;
        string wabaExternalId;
        string phoneExternalId;
        string displayPhone;
        Guid? effectiveWhatsAppAccountId = null;
        if (gatewayMode)
        {
            var whatsAppAccount = await whatsAppAccounts.ResolveAsync(
                projectId,
                request.WhatsAppAccountId,
                cancellationToken);
            if (whatsAppAccount is null)
                throw new AdvertisingException(
                    "ADS_WHATSAPP_ACCOUNT_NOT_FOUND",
                    "The selected WhatsApp account does not belong to this project.",
                    404);
            effectiveWhatsAppAccountId = whatsAppAccount.Id;

            var session = await gateway.GetAsync(
                projectId,
                effectiveWhatsAppAccountId.Value,
                cancellationToken);
            if (!session.Connected)
                throw new AdvertisingException("ADS_GATEWAY_NOT_CONNECTED", "Connect the project's WhatsApp Gateway before authorizing the advertising destination.", 409);
            phoneExternalId = WhatsAppGatewaySessionClient.NormalizePhone(session.PhoneNumber);
            var requestedPhone = WhatsAppGatewaySessionClient.NormalizePhone(request.PhoneNumberId);
            if (!string.IsNullOrWhiteSpace(requestedPhone) && !string.Equals(requestedPhone, phoneExternalId, StringComparison.Ordinal))
                throw new AdvertisingException("ADS_GATEWAY_PHONE_MISMATCH", "The selected phone does not match the live project Gateway session.", 409);
            wabaExternalId = effectiveWhatsAppAccountId.Value == projectId
                ? $"gateway:{projectId:N}"
                : $"gateway:{projectId:N}:{effectiveWhatsAppAccountId.Value:N}";
            displayPhone = $"+{phoneExternalId}";
        }
        else
        {
            dataset = catalog.Datasets.SingleOrDefault(x => x.Id == request.DatasetId);
            waba = catalog.Wabas.SingleOrDefault(x => x.Id == request.WabaId);
            phone = waba?.Phones.SingleOrDefault(x => x.Id == request.PhoneNumberId);
            if (dataset is null || waba is null || phone is null)
                throw new AdvertisingException("ADS_RESOURCES_NOT_MUTUALLY_ELIGIBLE", "The selected Meta, WhatsApp and Dataset resources are not mutually accessible.");
            wabaExternalId = waba.Id;
            phoneExternalId = phone.Id;
            displayPhone = phone.DisplayPhoneNumber;
        }

        var conflictingRoute = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.ProjectId != projectId && x.PhoneNumberExternalId == phoneExternalId
                           && x.State == AuthorizedDestinationState.Eligible,
                cancellationToken);
        if (conflictingRoute)
            throw new AdvertisingException("ADS_WHATSAPP_ROUTE_ALREADY_OWNED", "This WhatsApp receiving identity is already active in another project.", 409);

        var probe = gatewayMode
            ? await capabilities.ProbeGatewayAsync(token, request.AdAccountId, request.PageId, cancellationToken)
            : await capabilities.ProbeAsync(token, request.AdAccountId, request.PageId, phoneExternalId, cancellationToken);
        var destination = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.WabaExternalId == wabaExternalId && x.PhoneNumberExternalId == phoneExternalId, cancellationToken);
        if (destination is null)
        {
            destination = new AuthorizedWhatsAppDestination { ProjectId = projectId, ConnectionId = connection.Id };
            db.AdvertisingWhatsAppDestinations.Add(destination);
        }
        else destination.Version++;

        var snapshot = new AdvertisingCapabilitySnapshot
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            DestinationId = destination.Id,
            GraphApiVersion = "v26.0",
            ProviderAccountStatus = account.Status == 1 ? "Active" : "Inactive",
            PermissionStateJson = JsonSerializer.Serialize(catalog.GrantedPermissions),
            ObjectivesJson = probe.ObjectivesJson,
            OptimizationGoalsJson = probe.OptimizationGoalsJson,
            BidStrategiesJson = probe.BidStrategiesJson,
            PlacementEligibilityJson = probe.PlacementEligibilityJson,
            ValidationSupportJson = probe.ValidationSupportJson,
            ProbeEvidenceJson = JsonSerializer.Serialize<object>(gatewayMode
                ? new { account = account.Id, page = page.Id, gateway = "Baileys", phone = phoneExternalId, dataset = (string?)null }
                : new { account = account.Id, page = page.Id, waba = waba!.Id, phone = phone!.Id, dataset = dataset!.Id }),
            CheckedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(6),
            State = probe.Supported ? AdvertisingCapabilityState.Healthy : AdvertisingCapabilityState.Unsupported,
            ProviderTraceId = probe.Trace?.TraceId,
            FailureCode = string.IsNullOrWhiteSpace(probe.FailureCode) ? null : probe.FailureCode
        };
        db.AdvertisingCapabilitySnapshots.Add(snapshot);

        destination.Provider = gatewayMode ? "WhatsAppGateway" : "MetaWhatsApp";
        destination.WhatsAppAccountId = gatewayMode ? effectiveWhatsAppAccountId : null;
        destination.WabaExternalId = wabaExternalId;
        destination.PhoneNumberExternalId = phoneExternalId;
        destination.DisplayPhoneE164 = displayPhone;
        destination.PageExternalId = request.PageId;
        destination.DatasetExternalId = gatewayMode ? string.Empty : request.DatasetId;
        destination.ReceivingIdentityExternalId = phoneExternalId;
        destination.WhatsAppIntegrationMode = request.IntegrationMode;
        destination.MessagingState = "Ready";
        destination.AdvertisingState = probe.Supported ? "Ready" : "Unsupported";
        destination.BusinessEventsState = gatewayMode ? "NotApplicableGateway" : "PendingTest";
        destination.CapabilitySnapshotId = snapshot.Id;
        destination.LastValidatedAtUtc = DateTime.UtcNow;
        destination.State = probe.Supported ? AuthorizedDestinationState.Eligible : AuthorizedDestinationState.Ineligible;
        destination.LastErrorCode = probe.Supported ? null : probe.FailureCode;

        connection.AdAccountExternalId = request.AdAccountId;
        connection.PageExternalId = request.PageId;
        connection.WabaExternalId = gatewayMode ? null : request.WabaId;
        connection.DatasetExternalId = gatewayMode ? null : request.DatasetId;
        connection.AccountCurrency = account.Currency;
        connection.AccountTimezoneIana = account.Timezone;
        connection.WhatsAppIntegrationMode = request.IntegrationMode;
        connection.State = probe.Supported ? AdvertisingConnectionState.Ready : AdvertisingConnectionState.Degraded;
        connection.LastValidatedAtUtc = DateTime.UtcNow;
        connection.LastProviderTraceId = probe.Trace?.TraceId;
        connection.Version++;

        IntegrationOutbox.Enqueue(db, new AdvertisingWhatsAppDestinationChanged
        {
            ProjectId = projectId,
            DestinationId = destination.Id,
            DestinationVersion = destination.Version,
            WabaExternalId = destination.WabaExternalId,
            PhoneNumberExternalId = destination.PhoneNumberExternalId,
            IntegrationMode = destination.WhatsAppIntegrationMode.ToString(),
            State = destination.State == AuthorizedDestinationState.Eligible ? "Active" : "Revoked",
            SourceAggregateType = nameof(AuthorizedWhatsAppDestination),
            SourceAggregateId = destination.Id,
            SourceVersion = destination.Version,
            IsTombstone = destination.State != AuthorizedDestinationState.Eligible
        });
        audit.Append(new(projectId, "Connection", "WhatsAppDestinationAuthorized", nameof(AuthorizedWhatsAppDestination), destination.Id.ToString(),
            "User", connection.CreatedByUserId, JsonSerializer.Serialize(new { request.AdAccountId, request.PageId, integrationMode = request.IntegrationMode.ToString(), phone = phoneExternalId, state = destination.State.ToString() }), destination.Id));
        await db.SaveChangesAsync(cancellationToken);
        return new(connection.Id, destination.Id, snapshot.Id, destination.State.ToString());
    }

    public async Task<AdvertisingReadiness> RefreshAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        await RefreshExpiredCapabilityAsync(projectId, cancellationToken);
        var evaluationState = await LoadEvaluationStateAsync(projectId, cancellationToken);
        var gatewayReady = evaluationState.Destination?.WhatsAppIntegrationMode
            == WhatsAppIntegrationMode.BaileysObservedExperimental
            && await GatewayIsReadyAsync(projectId, evaluationState.Destination, cancellationToken);
        return Evaluate(evaluationState, gatewayReady);
    }

    public async Task<AdvertisingReadiness> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var evaluationState = await LoadEvaluationStateAsync(projectId, cancellationToken);
        return Evaluate(evaluationState, StoredGatewayIsReady(evaluationState.Destination));
    }

    public async Task<AdvertisingReadiness> GetForOverviewAsync(
        Guid projectId,
        AdvertisingReadinessOverviewState overviewState,
        CancellationToken cancellationToken = default)
    {
        var destinationContext = await LoadDestinationCapabilityContextAsync(projectId, cancellationToken);
        var hasEligibleOffer = await db.AdvertisingOffers.AsNoTracking()
            .AnyAsync(offer => offer.ProjectId == projectId && offer.State == "Eligible", cancellationToken);
        var evaluationState = new ReadinessEvaluationState(
            overviewState.Connection,
            destinationContext?.Destination,
            destinationContext?.Snapshot,
            overviewState.ActiveEnvelope,
            hasEligibleOffer,
            overviewState.HasTrackingIncident,
            overviewState.LatestTracking);
        return Evaluate(evaluationState, StoredGatewayIsReady(evaluationState.Destination));
    }

    private static AdvertisingReadiness Evaluate(
        ReadinessEvaluationState state,
        bool gatewayReady)
    {
        var now = DateTime.UtcNow;
        var facts = BuildReadinessFacts(state, now, gatewayReady);
        var readinessItems = ReadinessItems(state, facts, now);
        return new(readinessItems.All(readinessItem => readinessItem.Ready), readinessItems);
    }

    private static ReadinessFacts BuildReadinessFacts(
        ReadinessEvaluationState state,
        DateTime now,
        bool gatewayReady)
    {
        var gatewayMode = state.Destination?.WhatsAppIntegrationMode == WhatsAppIntegrationMode.BaileysObservedExperimental;
        var trackingReady = gatewayMode
            ? gatewayReady && !state.HasTrackingIncident
            : AdvertisingOperationalPolicy.HasFreshHealthyTracking(
                state.LatestTracking, state.HasTrackingIncident, now, TimeSpan.FromMinutes(30));
        var capability = state.Snapshot is null
            ? CapabilityDecision.Block("ADS_CAPABILITY_MISSING")
            : AdvertisingCapabilityPolicy.CanProvisionWhatsApp(state.Snapshot, now);
        return new(gatewayMode, gatewayReady, trackingReady, capability, HasValidEnvelope(state.ActiveEnvelope, now));
    }

    private static bool StoredGatewayIsReady(AuthorizedWhatsAppDestination? destination) =>
        destination?.WhatsAppIntegrationMode == WhatsAppIntegrationMode.BaileysObservedExperimental
        && string.Equals(destination.MessagingState, "Ready", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(destination.PhoneNumberExternalId);

    private async Task<bool> GatewayIsReadyAsync(
        Guid projectId,
        AuthorizedWhatsAppDestination destination,
        CancellationToken cancellationToken)
    {
        var liveGateway = await gateway.GetAsync(
            projectId,
            destination.WhatsAppAccountId ?? projectId,
            cancellationToken);
        return liveGateway.Connected
            && string.Equals(WhatsAppGatewaySessionClient.NormalizePhone(liveGateway.PhoneNumber),
                destination.PhoneNumberExternalId, StringComparison.Ordinal);
    }

    private static List<ReadinessItem> ReadinessItems(
        ReadinessEvaluationState state,
        ReadinessFacts facts,
        DateTime now) =>
    [
        new("connection", "ربط حساب Meta والصفحة", state.Connection?.State == AdvertisingConnectionState.Ready, state.Connection?.LastErrorCode),
        new("destination", facts.GatewayMode ? "Gateway WhatsApp متصل" : "وجهة WhatsApp موثقة",
            state.Destination?.State == AuthorizedDestinationState.Eligible && (!facts.GatewayMode || facts.GatewayReady),
            facts.GatewayMode && !facts.GatewayReady ? "ADS_GATEWAY_NOT_CONNECTED" : state.Destination?.LastErrorCode),
        new("capability", facts.GatewayMode ? "صلاحية Meta وAdvantage+ حديثة" : "صلاحية WhatsApp وAdvantage+ حديثة",
            facts.Capability.Ready, facts.Capability.Ready ? null : facts.Capability.Code),
        new("businessMessaging", facts.GatewayMode ? "وضع Gateway بدون WABA أو Dataset" : "Dataset داخل نفس ربط WhatsApp",
            facts.GatewayMode || !string.IsNullOrWhiteSpace(state.Destination?.DatasetExternalId),
            facts.GatewayMode ? null : string.IsNullOrWhiteSpace(state.Destination?.DatasetExternalId) ? "ADS_DATASET_REQUIRED" : null),
        new("offer", "عرض موثّق", state.HasEligibleOffer, state.HasEligibleOffer ? null : "ADS_OFFER_REQUIRED"),
        new("tracking", facts.GatewayMode ? "استقبال وقياس الليدز من Gateway" : "تتبع واتساب سليم", facts.TrackingReady,
            facts.GatewayMode ? facts.GatewayReady ? state.HasTrackingIncident ? "ADS_TRACKING_INCIDENT" : null : "ADS_GATEWAY_NOT_CONNECTED"
                : TrackingReadinessReason(state.LatestTracking, state.HasTrackingIncident, now)),
        new("budget", "تفويض ميزانية نشط", facts.EnvelopeReady,
            state.ActiveEnvelope is null ? "ADS_ENVELOPE_REQUIRED" : facts.EnvelopeReady ? null : "ADS_ENVELOPE_REAUTHORIZE")
    ];

    private async Task RefreshExpiredCapabilityAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var capabilityContext = await LoadCapabilityContextAsync(projectId, cancellationToken);
        var connection = capabilityContext.Connection;
        var destination = capabilityContext.Destination;
        if (capabilityContext.Snapshot is null
            || capabilityContext.Snapshot.ExpiresAtUtc > DateTime.UtcNow
            || connection?.ProtectedAccessToken is null
            || destination is null) return;

        try
        {
            await AuthorizeDestinationAsync(projectId, new AuthorizeWhatsAppDestinationRequest(
                connection.AdAccountExternalId ?? string.Empty,
                destination.PageExternalId,
                destination.WabaExternalId,
                destination.PhoneNumberExternalId,
                destination.DatasetExternalId,
                destination.WhatsAppIntegrationMode,
                destination.WhatsAppAccountId), cancellationToken);
        }
        catch (AdvertisingException)
        {
            // Stale provider evidence stays visible and readiness remains fail-closed.
        }
        catch (HttpRequestException)
        {
            // A provider outage cannot promote expired evidence to current evidence.
        }
    }

    private async Task<CapabilityReadContext> LoadCapabilityContextAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var connection = await db.AdvertisingConnections.AsNoTracking()
            .FirstOrDefaultAsync(row => row.ProjectId == projectId, cancellationToken);
        var destinationContext = await LoadDestinationCapabilityContextAsync(projectId, cancellationToken);
        return new(connection, destinationContext?.Destination, destinationContext?.Snapshot);
    }

    private async Task<ReadinessEvaluationState> LoadEvaluationStateAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var capabilityContext = await LoadCapabilityContextAsync(projectId, cancellationToken);
        var operationalContext = await LoadOperationalContextAsync(projectId, cancellationToken);
        return new(
            capabilityContext.Connection,
            capabilityContext.Destination,
            capabilityContext.Snapshot,
            operationalContext.ActiveEnvelope,
            operationalContext.HasEligibleOffer,
            operationalContext.HasTrackingIncident,
            operationalContext.LatestTracking);
    }

    private async Task<DestinationCapabilityContext?> LoadDestinationCapabilityContextAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        return await (from destination in db.AdvertisingWhatsAppDestinations.AsNoTracking()
            join snapshot in db.AdvertisingCapabilitySnapshots.AsNoTracking()
                on destination.CapabilitySnapshotId equals (Guid?)snapshot.Id into snapshots
            from snapshot in snapshots.DefaultIfEmpty()
            where destination.ProjectId == projectId
            orderby destination.LastValidatedAtUtc descending
            select new DestinationCapabilityContext(destination, snapshot))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<ReadinessOperationalContext> LoadOperationalContextAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var operationalContext = await db.ProjectAdvertisingContextProjections.AsNoTracking()
            .Where(context => context.ProjectId == projectId)
            .Select(_ => new ReadinessOperationalContext(
                db.AutonomyEnvelopes.AsNoTracking().FirstOrDefault(envelope =>
                    envelope.ProjectId == projectId && envelope.State == EnvelopeState.Active),
                db.AdvertisingOffers.AsNoTracking().Any(offer =>
                    offer.ProjectId == projectId && offer.State == "Eligible"),
                db.TrackingIncidents.AsNoTracking().Any(incident => incident.ProjectId == projectId
                    && incident.Category == "ConversionTracking" && incident.State != IncidentState.Recovered),
                db.AdvertisingTrackingHealthSnapshots.AsNoTracking()
                    .Where(snapshot => snapshot.ProjectId == projectId)
                    .OrderByDescending(snapshot => snapshot.EvaluatedAtUtc)
                    .FirstOrDefault()))
            .SingleOrDefaultAsync(cancellationToken);

        return operationalContext ?? await LoadOperationalContextFallbackAsync(projectId, cancellationToken);
    }

    private async Task<ReadinessOperationalContext> LoadOperationalContextFallbackAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var activeEnvelope = await db.AutonomyEnvelopes.AsNoTracking().FirstOrDefaultAsync(envelope =>
            envelope.ProjectId == projectId && envelope.State == EnvelopeState.Active, cancellationToken);
        var hasEligibleOffer = await db.AdvertisingOffers.AsNoTracking().AnyAsync(offer =>
            offer.ProjectId == projectId && offer.State == "Eligible", cancellationToken);
        var hasTrackingIncident = await db.TrackingIncidents.AsNoTracking().AnyAsync(incident =>
            incident.ProjectId == projectId && incident.Category == "ConversionTracking"
                && incident.State != IncidentState.Recovered, cancellationToken);
        var latestTracking = await db.AdvertisingTrackingHealthSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.ProjectId == projectId)
            .OrderByDescending(snapshot => snapshot.EvaluatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return new(activeEnvelope, hasEligibleOffer, hasTrackingIncident, latestTracking);
    }

    private sealed record DestinationCapabilityContext(
        AuthorizedWhatsAppDestination Destination,
        AdvertisingCapabilitySnapshot? Snapshot);

    private sealed record CapabilityReadContext(
        AdvertisingConnection? Connection,
        AuthorizedWhatsAppDestination? Destination,
        AdvertisingCapabilitySnapshot? Snapshot);

    private sealed record ReadinessOperationalContext(
        AutonomyEnvelope? ActiveEnvelope,
        bool HasEligibleOffer,
        bool HasTrackingIncident,
        TrackingHealthSnapshot? LatestTracking);

    private sealed record ReadinessEvaluationState(
        AdvertisingConnection? Connection,
        AuthorizedWhatsAppDestination? Destination,
        AdvertisingCapabilitySnapshot? Snapshot,
        AutonomyEnvelope? ActiveEnvelope,
        bool HasEligibleOffer,
        bool HasTrackingIncident,
        TrackingHealthSnapshot? LatestTracking);

    private sealed record ReadinessFacts(
        bool GatewayMode,
        bool GatewayReady,
        bool TrackingReady,
        CapabilityDecision Capability,
        bool EnvelopeReady);

    private async Task<AdvertisingConnection> ConnectionWithToken(Guid projectId, CancellationToken cancellationToken) =>
        await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.ProtectedAccessToken != null, cancellationToken)
        ?? throw new AdvertisingException("ADS_OAUTH_REQUIRED", "Connect Meta before selecting resources.", 409);

    private static string? TrackingReadinessReason(TrackingHealthSnapshot? snapshot, bool hasOpenIncident, DateTime nowUtc) =>
        hasOpenIncident ? "ADS_TRACKING_INCIDENT"
        : snapshot is null ? "ADS_TRACKING_SNAPSHOT_REQUIRED"
        : snapshot.State != TrackingHealthState.Healthy ? "ADS_TRACKING_UNSAFE"
        : snapshot.EvaluatedAtUtc < nowUtc.AddMinutes(-30) ? "ADS_TRACKING_STALE"
        : null;

    private static bool HasValidEnvelope(AutonomyEnvelope? envelope, DateTime nowUtc)
    {
        if (envelope is null || envelope.PeriodCap is null || envelope.StartsAtUtc > nowUtc || envelope.EndsAtUtc < nowUtc) return false;
        var definition = new AutonomyEnvelopeDefinition(envelope.DailyCap, envelope.PeriodCap, envelope.PeriodCapKind,
            envelope.Currency, JsonSerializer.Deserialize<string[]>(envelope.HardIncludedGeoJson) ?? [],
            JsonSerializer.Deserialize<string[]>(envelope.HardExcludedGeoJson) ?? [], envelope.HardMinimumAge,
            JsonSerializer.Deserialize<string[]>(envelope.HardRequiredLanguagesJson) ?? [],
            JsonSerializer.Deserialize<string[]>(envelope.HardCustomAudienceExclusionsJson) ?? [], envelope.ReportingTimezoneIana);
        return AutonomyEnvelopePolicy.Validate(definition).IsValid;
    }
}
