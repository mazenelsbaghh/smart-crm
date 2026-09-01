using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class RebuildWhatsAppAiMediaBuyer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdvertisingInsights_ProjectId_TargetId_IntervalStartUtc_Int~",
                table: "AdvertisingInsights");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisingBudgetLedgers_ProjectId_EnvelopeId_PeriodStartUtc",
                table: "AdvertisingBudgetLedgers");

            migrationBuilder.AddColumn<long>(
                name: "AdvertisingContextVersion",
                table: "ProjectSettings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "AdSetId",
                table: "ManagedAdvertisements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "ManagedAdvertisements",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                table: "ManagedAdvertisements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DestinationId",
                table: "ManagedAdvertisements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationType",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EffectiveStateHash",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExperimentArmId",
                table: "ManagedAdvertisements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ManagedProviderCreativeId",
                table: "ManagedAdvertisements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnershipRecordId",
                table: "ManagedAdvertisements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "ManagedAdvertisements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlannedStateHash",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ReconciliationState",
                table: "ManagedAdvertisements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReviewStatus",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ProcessedAtUtc",
                table: "IntegrationInboxReceipts",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "FailureCode",
                table: "IntegrationInboxReceipts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "IntegrationInboxReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAtUtc",
                table: "IntegrationInboxReceipts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "SourceAggregateId",
                table: "IntegrationInboxReceipts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "SourceAggregateType",
                table: "IntegrationInboxReceipts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "SourceVersion",
                table: "IntegrationInboxReceipts",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "IntegrationInboxReceipts",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AttributionWindowDays",
                table: "AutonomyEnvelopes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AudienceBoundaryHash",
                table: "AutonomyEnvelopes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AutonomyEnvelopes",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "DefinitionHash",
                table: "AutonomyEnvelopes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HardCustomAudienceExclusionsJson",
                table: "AutonomyEnvelopes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HardExcludedGeoJson",
                table: "AutonomyEnvelopes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HardIncludedGeoJson",
                table: "AutonomyEnvelopes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "HardMinimumAge",
                table: "AutonomyEnvelopes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HardRequiredLanguagesJson",
                table: "AutonomyEnvelopes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PlacementPolicy",
                table: "AutonomyEnvelopes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReportingTimezoneIana",
                table: "AutonomyEnvelopes",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "TimezoneSnapshotAtUtc",
                table: "AutonomyEnvelopes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimezoneSource",
                table: "AutonomyEnvelopes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProjectionVersion",
                table: "Assets",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "OverlapEndsAtUtc",
                table: "AdvertisingWebhookSources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplayEvidenceJson",
                table: "AdvertisingWebhookSources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevokedAtUtc",
                table: "AdvertisingWebhookSources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RotatedAtUtc",
                table: "AdvertisingWebhookSources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "AdvertisingWebhookSources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "AdvertisingWebhookSources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AudienceFactsJson",
                table: "AdvertisingProfiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ModelVersion",
                table: "AdvertisingProfiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PromptVersion",
                table: "AdvertisingProfiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AttributionWindowDays",
                table: "AdvertisingOffers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CapacityUpdatedAtUtc",
                table: "AdvertisingOffers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContributionMargin",
                table: "AdvertisingOffers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CurrentCapacity",
                table: "AdvertisingOffers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DailyCapacity",
                table: "AdvertisingOffers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FallbackOutcomeOrderJson",
                table: "AdvertisingOffers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "MaximumSustainableCost",
                table: "AdvertisingOffers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PolicyEvidenceJson",
                table: "AdvertisingOffers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PolicyState",
                table: "AdvertisingOffers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryOutcome",
                table: "AdvertisingOffers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScheduleJson",
                table: "AdvertisingOffers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SpecialAdCategory",
                table: "AdvertisingOffers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "AdvertisingOffers",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountTimezone",
                table: "AdvertisingInsights",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AttributionSetting",
                table: "AdvertisingInsights",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BreakdownHash",
                table: "AdvertisingInsights",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ConnectionId",
                table: "AdvertisingInsights",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "AdvertisingInsights",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "FetchRunId",
                table: "AdvertisingInsights",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "AdvertisingInsights",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LearningStatus",
                table: "AdvertisingInsights",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlacementBreakdownJson",
                table: "AdvertisingInsights",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProviderActionValuesJson",
                table: "AdvertisingInsights",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "Reach",
                table: "AdvertisingInsights",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "AdvertisingInsights",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceFreshnessUtc",
                table: "AdvertisingInsights",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersedesSnapshotId",
                table: "AdvertisingInsights",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Confidence",
                table: "AdvertisingFactSources",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsContradictory",
                table: "AdvertisingFactSources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsRequiredForLaunch",
                table: "AdvertisingFactSources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedValueJson",
                table: "AdvertisingFactSources",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ObservedAtUtc",
                table: "AdvertisingFactSources",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "SourceVersion",
                table: "AdvertisingFactSources",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClaimedAtUtc",
                table: "AdvertisingExecutionCommands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "AdvertisingExecutionCommands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AdvertisingExecutionCommands",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconciledAtUtc",
                table: "AdvertisingExecutionCommands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconciliationEvidenceJson",
                table: "AdvertisingExecutionCommands",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAtUtc",
                table: "AdvertisingExecutionCommands",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AdvertisingEmergencyStops",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ProgressJson",
                table: "AdvertisingEmergencyStops",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "AdvertisingEmergencyStops",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AdvertisingDecisions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "EnvelopeId",
                table: "AdvertisingDecisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvidenceHash",
                table: "AdvertisingDecisions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ExecutionCommandId",
                table: "AdvertisingDecisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "AdvertisingDecisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCodesJson",
                table: "AdvertisingDecisions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ModelVersion",
                table: "AdvertisingDecisionReviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromptVersion",
                table: "AdvertisingDecisionReviews",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAtUtc",
                table: "AdvertisingDecisionReviews",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "BucketEndUtc",
                table: "AdvertisingCycleRuns",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "LeaseOwner",
                table: "AdvertisingCycleRuns",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReportingTimezoneIana",
                table: "AdvertisingCycleRuns",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CallToAction",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "AdvertisingCreativeVariants",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GeneratedAtUtc",
                table: "AdvertisingCreativeVariants",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Headline",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfferFactHash",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PageCompatibilityJson",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PlacementFormat",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryText",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailObjectKey",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppDestinationCompatibilityJson",
                table: "AdvertisingCreativeVariants",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "AdvertisingCreatives",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConceptKey",
                table: "AdvertisingCreatives",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HookKey",
                table: "AdvertisingCreatives",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrganicEvidenceJson",
                table: "AdvertisingCreatives",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PaidEvidenceJson",
                table: "AdvertisingCreatives",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RecommendationBand",
                table: "AdvertisingCreatives",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BusinessAggregateId",
                table: "AdvertisingConversionSourceEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "BusinessAggregateType",
                table: "AdvertisingConversionSourceEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ConsentEvidenceJson",
                table: "AdvertisingConversionSourceEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "AdvertisingConversionSourceEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventType",
                table: "AdvertisingConversionSourceEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "JourneyLocation",
                table: "AdvertisingConversionSourceEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedPayloadJson",
                table: "AdvertisingConversionSourceEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "OccurredAtUtc",
                table: "AdvertisingConversionSourceEvents",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<decimal>(
                name: "Value",
                table: "AdvertisingConversionSourceEvents",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttributionState",
                table: "AdvertisingConversions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "AttributionTouchId",
                table: "AdvertisingConversions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttributionWindowEndsAtUtc",
                table: "AdvertisingConversions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AdvertisingConversions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "CorrectionState",
                table: "AdvertisingConversions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceHistoryJson",
                table: "AdvertisingConversions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TruthState",
                table: "AdvertisingConversions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "DeliveryId",
                table: "AdvertisingConversionDeliveryAttempts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EventsReceived",
                table: "AdvertisingConversionDeliveryAttempts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                table: "AdvertisingConversionDeliveryAttempts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderRequestId",
                table: "AdvertisingConversionDeliveryAttempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderTraceId",
                table: "AdvertisingConversionDeliveryAttempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResponseHash",
                table: "AdvertisingConversionDeliveryAttempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarningsJson",
                table: "AdvertisingConversionDeliveryAttempts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceEventId",
                table: "AdvertisingConversionAdjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountStatus",
                table: "AdvertisingConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AccountTimezoneIana",
                table: "AdvertisingConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AdvertisingConnections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "FundingStatus",
                table: "AdvertisingConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrantedPermissionsJson",
                table: "AdvertisingConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "GraphApiVersion",
                table: "AdvertisingConnections",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "LastProviderTraceId",
                table: "AdvertisingConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReferralProofAtUtc",
                table: "AdvertisingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferralProofHash",
                table: "AdvertisingConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReferralProofState",
                table: "AdvertisingConnections",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TimezoneSource",
                table: "AdvertisingConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TimezoneValidatedAtUtc",
                table: "AdvertisingConnections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WabaExternalId",
                table: "AdvertisingConnections",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WhatsAppIntegrationMode",
                table: "AdvertisingConnections",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AdvertisingBudgetLedgers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "DelayedSpendEstimate",
                table: "AdvertisingBudgetLedgers",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "EnvelopeVersion",
                table: "AdvertisingBudgetLedgers",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<decimal>(
                name: "ForecastSpend",
                table: "AdvertisingBudgetLedgers",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PeriodKind",
                table: "AdvertisingBudgetLedgers",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyToken",
                table: "AdvertisingBudgetAllocations",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "ExperimentId",
                table: "AdvertisingBudgetAllocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalBudgetOwnerId",
                table: "AdvertisingBudgetAllocations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "AdvertisingBudgetAllocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetType",
                table: "AdvertisingBudgetAllocations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "AttributionContextId",
                table: "AdvertisingAttributionTouches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "AdvertisingAttributionTouches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DestinationId",
                table: "AdvertisingAttributionTouches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EligibilityEvidenceJson",
                table: "AdvertisingAttributionTouches",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "JourneyKey",
                table: "AdvertisingAttributionTouches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ObservationId",
                table: "AdvertisingAttributionTouches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedCtwaClid",
                table: "AdvertisingAttributionTouches",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderAdExternalId",
                table: "AdvertisingAttributionTouches",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdvertisingAiWorkItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<string>(type: "text", nullable: false),
                    InputVersion = table.Column<string>(type: "text", nullable: false),
                    InputHash = table.Column<string>(type: "text", nullable: false),
                    InputJson = table.Column<string>(type: "text", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    DeadlineUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    ModelVersion = table.Column<string>(type: "text", nullable: true),
                    FailureCode = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingAiWorkItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingAttributionContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    JourneyKey = table.Column<string>(type: "text", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ObservationCount = table.Column<int>(type: "integer", nullable: false),
                    ValidReferralCount = table.Column<int>(type: "integer", nullable: false),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingAttributionContexts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingAttributionObservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    JourneyKey = table.Column<string>(type: "text", nullable: false),
                    MessageExternalId = table.Column<string>(type: "text", nullable: false),
                    MessageOccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationVersion = table.Column<long>(type: "bigint", nullable: false),
                    ReceivingIdentityExternalId = table.Column<string>(type: "text", nullable: false),
                    IntegrationMode = table.Column<int>(type: "integer", nullable: false),
                    IdentifierState = table.Column<int>(type: "integer", nullable: false),
                    ProtectedCtwaClid = table.Column<string>(type: "text", nullable: true),
                    ProtectionPurpose = table.Column<string>(type: "text", nullable: true),
                    CtwaClidHash = table.Column<string>(type: "text", nullable: true),
                    OpaquePayloadHash = table.Column<string>(type: "text", nullable: true),
                    ProviderAdExternalId = table.Column<string>(type: "text", nullable: true),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    GatewayType = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingAttributionObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingAudienceSourceGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvelopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    SourceExternalId = table.Column<string>(type: "text", nullable: false),
                    SourceLabel = table.Column<string>(type: "text", nullable: false),
                    AllowedUsesJson = table.Column<string>(type: "text", nullable: false),
                    ConsentState = table.Column<int>(type: "integer", nullable: false),
                    LegalBasis = table.Column<string>(type: "text", nullable: true),
                    LegalBasisRecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingAudienceSourceGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingAudienceStrategies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvelopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IncludedGeoJson = table.Column<string>(type: "text", nullable: false),
                    ExcludedGeoJson = table.Column<string>(type: "text", nullable: false),
                    MinimumAge = table.Column<int>(type: "integer", nullable: false),
                    MaximumAgeSuggestion = table.Column<int>(type: "integer", nullable: true),
                    RequiredLanguagesJson = table.Column<string>(type: "text", nullable: false),
                    CustomAudienceExclusionsJson = table.Column<string>(type: "text", nullable: false),
                    AudienceSuggestionsJson = table.Column<string>(type: "text", nullable: false),
                    AuthorizedSourceGrantIdsJson = table.Column<string>(type: "text", nullable: false),
                    SpecialCategoryConstraintsJson = table.Column<string>(type: "text", nullable: false),
                    EstimatedReachJson = table.Column<string>(type: "text", nullable: false),
                    DefinitionHash = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingAudienceStrategies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingAuditRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorType = table.Column<string>(type: "text", nullable: false),
                    SafeEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IndexState = table.Column<string>(type: "text", nullable: false),
                    IndexAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextIndexAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastIndexErrorCode = table.Column<string>(type: "text", nullable: true),
                    IndexedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingAuditRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingBudgetAllocationDebits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingBudgetAllocationDebits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingCampaignPlanCreatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreativeVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    ConceptKey = table.Column<string>(type: "text", nullable: false),
                    HookKey = table.Column<string>(type: "text", nullable: false),
                    PlacementCompatibilityJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingCampaignPlanCreatives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingCampaignPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvelopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvelopeVersion = table.Column<long>(type: "bigint", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilitySnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    BusinessGoal = table.Column<string>(type: "text", nullable: false),
                    Objective = table.Column<string>(type: "text", nullable: false),
                    OptimizationGoal = table.Column<string>(type: "text", nullable: false),
                    OptimizationFallbackOrderJson = table.Column<string>(type: "text", nullable: false),
                    BidStrategy = table.Column<string>(type: "text", nullable: false),
                    BudgetMode = table.Column<string>(type: "text", nullable: false),
                    DailyBudget = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SpecialAdCategory = table.Column<string>(type: "text", nullable: true),
                    PlacementMode = table.Column<int>(type: "integer", nullable: false),
                    AudienceStrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanJson = table.Column<string>(type: "text", nullable: false),
                    PlanHash = table.Column<string>(type: "text", nullable: false),
                    ReadinessJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingCampaignPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingCapabilitySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GraphApiVersion = table.Column<string>(type: "text", nullable: false),
                    ProviderAccountStatus = table.Column<string>(type: "text", nullable: false),
                    PermissionStateJson = table.Column<string>(type: "text", nullable: false),
                    ObjectivesJson = table.Column<string>(type: "text", nullable: false),
                    OptimizationGoalsJson = table.Column<string>(type: "text", nullable: false),
                    BidStrategiesJson = table.Column<string>(type: "text", nullable: false),
                    PlacementEligibilityJson = table.Column<string>(type: "text", nullable: false),
                    AutomationFeaturesJson = table.Column<string>(type: "text", nullable: false),
                    ValidationSupportJson = table.Column<string>(type: "text", nullable: false),
                    ProductionAccessJson = table.Column<string>(type: "text", nullable: false),
                    ProbeEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    SupportedValidationObjectsJson = table.Column<string>(type: "text", nullable: false),
                    ProviderFieldsVersion = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    CheckedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ProviderTraceId = table.Column<string>(type: "text", nullable: true),
                    FailureCode = table.Column<string>(type: "text", nullable: true),
                    FailureSummary = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingCapabilitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingConversionDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    EventIdentity = table.Column<string>(type: "text", nullable: false),
                    EventName = table.Column<string>(type: "text", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SuppressionReason = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingConversionDeliveries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingDecisionImpacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaselineWindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BaselineWindowEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvaluationWindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvaluationWindowEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Goal = table.Column<string>(type: "text", nullable: false),
                    BaselineEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    EvaluationEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    Label = table.Column<int>(type: "integer", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RollbackCommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingDecisionImpacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingDisableRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContinuingSpendAcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ProgressJson = table.Column<string>(type: "text", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingDisableRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingDisconnectOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ContinuingOrUnmonitoredSpendAcknowledgedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmergencyStopRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                    CredentialDisposedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RouteTombstoneVersion = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    RecoveryInstruction = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingDisconnectOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingDisconnectTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisconnectOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderExternalId = table.Column<string>(type: "text", nullable: false),
                    DesiredState = table.Column<string>(type: "text", nullable: false),
                    ProviderOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReadBackState = table.Column<string>(type: "text", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingDisconnectTargets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingExperimentArms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsControl = table.Column<bool>(type: "boolean", nullable: false),
                    ChangedValueJson = table.Column<string>(type: "text", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedTargetType = table.Column<string>(type: "text", nullable: false),
                    ManagedTargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    AllocatedBudget = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingExperimentArms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingExperimentEvaluations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExperimentId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttributionCutoffUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Goal = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    Coverage = table.Column<decimal>(type: "numeric", nullable: false),
                    SampleSize = table.Column<int>(type: "integer", nullable: false),
                    Verdict = table.Column<string>(type: "text", nullable: false),
                    ReasonCodesJson = table.Column<string>(type: "text", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingExperimentEvaluations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingExperiments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvelopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Hypothesis = table.Column<string>(type: "text", nullable: false),
                    PrimaryVariable = table.Column<string>(type: "text", nullable: false),
                    BusinessOutcome = table.Column<string>(type: "text", nullable: false),
                    AttributionWindowDays = table.Column<int>(type: "integer", nullable: false),
                    MinimumElapsedHours = table.Column<int>(type: "integer", nullable: false),
                    MinimumSpend = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MinimumAttributedOutcomes = table.Column<int>(type: "integer", nullable: false),
                    MinimumAttributionCoverage = table.Column<decimal>(type: "numeric", nullable: false),
                    CorrectionLagHours = table.Column<int>(type: "integer", nullable: false),
                    ConfidencePolicyJson = table.Column<string>(type: "text", nullable: false),
                    BudgetCap = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    StopRuleJson = table.Column<string>(type: "text", nullable: false),
                    DefinitionHash = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MaturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StoppedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConclusionJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingExperiments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingKnowledgeProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentVersion = table.Column<long>(type: "bigint", nullable: false),
                    RevisionHash = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    SafeFactsJson = table.Column<string>(type: "text", nullable: false),
                    AffectedOfferKeysJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedFromEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsTombstoned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingKnowledgeProjections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingManagedAdSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    CampaignId = table.Column<Guid>(type: "uuid", nullable: false),
                    AudienceStrategyId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExperimentArmId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ConfiguredStatus = table.Column<string>(type: "text", nullable: false),
                    EffectiveStatus = table.Column<string>(type: "text", nullable: false),
                    ReviewStatus = table.Column<string>(type: "text", nullable: true),
                    ReconciliationState = table.Column<int>(type: "integer", nullable: false),
                    PlannedStateHash = table.Column<string>(type: "text", nullable: false),
                    EffectiveStateHash = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastProviderErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastProviderErrorSummary = table.Column<string>(type: "text", nullable: true),
                    OptimizationGoal = table.Column<string>(type: "text", nullable: false),
                    DestinationType = table.Column<string>(type: "text", nullable: false),
                    PromotedPageExternalId = table.Column<string>(type: "text", nullable: false),
                    PromotedWhatsAppPhoneExternalId = table.Column<string>(type: "text", nullable: false),
                    AttributionSetting = table.Column<string>(type: "text", nullable: false),
                    PlacementMode = table.Column<int>(type: "integer", nullable: false),
                    DailyBudget = table.Column<decimal>(type: "numeric", nullable: true),
                    LifetimeBudget = table.Column<decimal>(type: "numeric", nullable: true),
                    BudgetOwnerExternalId = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingManagedAdSets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingManagedCampaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ConfiguredStatus = table.Column<string>(type: "text", nullable: false),
                    EffectiveStatus = table.Column<string>(type: "text", nullable: false),
                    ReviewStatus = table.Column<string>(type: "text", nullable: true),
                    ReconciliationState = table.Column<int>(type: "integer", nullable: false),
                    PlannedStateHash = table.Column<string>(type: "text", nullable: false),
                    EffectiveStateHash = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastProviderErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastProviderErrorSummary = table.Column<string>(type: "text", nullable: true),
                    Objective = table.Column<string>(type: "text", nullable: false),
                    BuyingType = table.Column<string>(type: "text", nullable: false),
                    SpecialAdCategory = table.Column<string>(type: "text", nullable: true),
                    BudgetMode = table.Column<string>(type: "text", nullable: false),
                    DailyBudget = table.Column<decimal>(type: "numeric", nullable: true),
                    LifetimeBudget = table.Column<decimal>(type: "numeric", nullable: true),
                    BidStrategy = table.Column<string>(type: "text", nullable: false),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingManagedCampaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingManagedOwnership",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RootManagedCampaignId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderCampaignExternalId = table.Column<string>(type: "text", nullable: false),
                    OwnershipKind = table.Column<int>(type: "integer", nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ImportEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    AllowedMutationScopeJson = table.Column<string>(type: "text", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingManagedOwnership", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingManagedProviderCreatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnershipRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertisingCreativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreativeVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SourceType = table.Column<string>(type: "text", nullable: false),
                    ObjectStoryExternalId = table.Column<string>(type: "text", nullable: true),
                    ProviderCreativeType = table.Column<string>(type: "text", nullable: false),
                    PageExternalId = table.Column<string>(type: "text", nullable: false),
                    WhatsAppPhoneExternalId = table.Column<string>(type: "text", nullable: false),
                    CallToAction = table.Column<string>(type: "text", nullable: false),
                    VerificationState = table.Column<int>(type: "integer", nullable: false),
                    PlannedStateHash = table.Column<string>(type: "text", nullable: false),
                    EffectiveStateHash = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastProviderErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastProviderErrorSummary = table.Column<string>(type: "text", nullable: true),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingManagedProviderCreatives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingMediaProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetVersion = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    FileHash = table.Column<string>(type: "text", nullable: false),
                    ObjectReference = table.Column<string>(type: "text", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    RightsState = table.Column<string>(type: "text", nullable: false),
                    BrandMetadataJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedFromEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsTombstoned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingMediaProjections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingOfferDestinationGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvelopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllowedFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AllowedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MaximumDailyAllocation = table.Column<decimal>(type: "numeric", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingOfferDestinationGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingProjectionBackfillRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    Phase = table.Column<string>(type: "text", nullable: false),
                    CursorJson = table.Column<string>(type: "text", nullable: false),
                    ParityJson = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingProjectionBackfillRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingProviderObjectSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectType = table.Column<string>(type: "text", nullable: false),
                    LocalObjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderObjectId = table.Column<string>(type: "text", nullable: true),
                    SnapshotType = table.Column<string>(type: "text", nullable: false),
                    NormalizedStateJson = table.Column<string>(type: "text", nullable: false),
                    StateHash = table.Column<string>(type: "text", nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GraphApiVersion = table.Column<string>(type: "text", nullable: false),
                    ProviderTraceId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingProviderObjectSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingProviderOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommandId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationType = table.Column<string>(type: "text", nullable: false),
                    TargetType = table.Column<string>(type: "text", nullable: false),
                    LocalTargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderTargetId = table.Column<string>(type: "text", nullable: true),
                    DependsOnOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "text", nullable: false),
                    GraphApiVersion = table.Column<string>(type: "text", nullable: false),
                    PlannedPayloadJson = table.Column<string>(type: "text", nullable: false),
                    ResponseFingerprint = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwner = table.Column<string>(type: "text", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderRequestId = table.Column<string>(type: "text", nullable: true),
                    ProviderTraceId = table.Column<string>(type: "text", nullable: true),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    ErrorSubcode = table.Column<string>(type: "text", nullable: true),
                    ErrorSummary = table.Column<string>(type: "text", nullable: true),
                    Retryable = table.Column<bool>(type: "boolean", nullable: false),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingProviderOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingProviderValidationFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Stage = table.Column<string>(type: "text", nullable: false),
                    ObjectType = table.Column<string>(type: "text", nullable: false),
                    ObjectId = table.Column<string>(type: "text", nullable: true),
                    Field = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    ProviderCode = table.Column<string>(type: "text", nullable: true),
                    ProviderSubcode = table.Column<string>(type: "text", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    NextSafeAction = table.Column<string>(type: "text", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolutionOperationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingProviderValidationFindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingTrackingHealthSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingHealthPolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingHealthPolicyVersion = table.Column<int>(type: "integer", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    InboundConversationCount = table.Column<int>(type: "integer", nullable: false),
                    ReferralObservationCount = table.Column<int>(type: "integer", nullable: false),
                    ValidReferralCount = table.Column<int>(type: "integer", nullable: false),
                    ReferralCoverage = table.Column<decimal>(type: "numeric", nullable: true),
                    ExactMatchRate = table.Column<decimal>(type: "numeric", nullable: true),
                    ProviderMatchQuality = table.Column<decimal>(type: "numeric", nullable: true),
                    DeliveryAcceptanceRate = table.Column<decimal>(type: "numeric", nullable: true),
                    CorrectionRate = table.Column<decimal>(type: "numeric", nullable: true),
                    MissingReferralRate = table.Column<decimal>(type: "numeric", nullable: true),
                    EventDelayMinutesP95 = table.Column<double>(type: "double precision", nullable: true),
                    SourceFreshnessUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ReasonCodesJson = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingTrackingHealthSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingTrackingPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Goal = table.Column<string>(type: "text", nullable: false),
                    MinimumDenominator = table.Column<int>(type: "integer", nullable: false),
                    MinimumReferralCoverage = table.Column<decimal>(type: "numeric", nullable: false),
                    MinimumExactMatchRate = table.Column<decimal>(type: "numeric", nullable: false),
                    MinimumDeliveryAcceptanceRate = table.Column<decimal>(type: "numeric", nullable: false),
                    MaximumCorrectionRate = table.Column<decimal>(type: "numeric", nullable: false),
                    MaximumEventDelayMinutes = table.Column<int>(type: "integer", nullable: false),
                    DefinitionHash = table.Column<string>(type: "text", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingTrackingPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingWhatsAppDestinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    WabaExternalId = table.Column<string>(type: "text", nullable: false),
                    PhoneNumberExternalId = table.Column<string>(type: "text", nullable: false),
                    DisplayPhoneE164 = table.Column<string>(type: "text", nullable: true),
                    PageExternalId = table.Column<string>(type: "text", nullable: false),
                    DatasetExternalId = table.Column<string>(type: "text", nullable: false),
                    ReceivingIdentityExternalId = table.Column<string>(type: "text", nullable: false),
                    WhatsAppIntegrationMode = table.Column<int>(type: "integer", nullable: false),
                    MessagingState = table.Column<string>(type: "text", nullable: false),
                    AdvertisingState = table.Column<string>(type: "text", nullable: false),
                    BusinessEventsState = table.Column<string>(type: "text", nullable: false),
                    ReferralCaptureState = table.Column<int>(type: "integer", nullable: false),
                    ReferralProofAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CapabilitySnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ConcurrencyToken = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingWhatsAppDestinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAdvertisingConsentProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsentVersion = table.Column<long>(type: "bigint", nullable: false),
                    ConsentState = table.Column<string>(type: "text", nullable: false),
                    LegalBasis = table.Column<string>(type: "text", nullable: false),
                    EffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedFromEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsTombstoned = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAdvertisingConsentProjections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationProjectionWatermarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "text", nullable: false),
                    SourceAggregateType = table.Column<string>(type: "text", nullable: false),
                    SourceAggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentVersion = table.Column<long>(type: "bigint", nullable: false),
                    IsTombstoned = table.Column<bool>(type: "boolean", nullable: false),
                    MissingFromVersion = table.Column<long>(type: "bigint", nullable: true),
                    MissingToVersion = table.Column<long>(type: "bigint", nullable: true),
                    LastEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationProjectionWatermarks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectAdvertisingContextProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    LifecycleState = table.Column<string>(type: "text", nullable: false),
                    ReportingTimezoneIana = table.Column<string>(type: "text", nullable: false),
                    AiConfigurationVersion = table.Column<long>(type: "bigint", nullable: false),
                    AllowedAiModel = table.Column<string>(type: "text", nullable: false),
                    AiSettingsHash = table.Column<string>(type: "text", nullable: false),
                    UpdatedFromEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceVersion = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectAdvertisingContextProjections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WhatsAppInboundRouteProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationVersion = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    WabaExternalId = table.Column<string>(type: "text", nullable: false),
                    PhoneNumberExternalId = table.Column<string>(type: "text", nullable: false),
                    IntegrationMode = table.Column<string>(type: "text", nullable: false),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAggregateVersion = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppInboundRouteProjections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingInsights_ProjectId_TargetType_TargetId_Interval~1",
                table: "AdvertisingInsights",
                columns: new[] { "ProjectId", "TargetType", "TargetId", "IntervalStartUtc", "IntervalEndUtc", "BreakdownHash", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingInsights_ProjectId_TargetType_TargetId_IntervalS~",
                table: "AdvertisingInsights",
                columns: new[] { "ProjectId", "TargetType", "TargetId", "IntervalStartUtc", "IntervalEndUtc", "BreakdownHash", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingConversionDeliveryAttempts_DeliveryId_AttemptNum~",
                table: "AdvertisingConversionDeliveryAttempts",
                columns: new[] { "DeliveryId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingConnections_Provider_AdAccountExternalId",
                table: "AdvertisingConnections",
                columns: new[] { "Provider", "AdAccountExternalId" },
                unique: true,
                filter: "\"AdAccountExternalId\" IS NOT NULL AND \"State\" <> 5");

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingBudgetLedgers_ProjectId_EnvelopeId_PeriodKind_Pe~",
                table: "AdvertisingBudgetLedgers",
                columns: new[] { "ProjectId", "EnvelopeId", "PeriodKind", "PeriodStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingAttributionTouches_ProjectId_ObservationId",
                table: "AdvertisingAttributionTouches",
                columns: new[] { "ProjectId", "ObservationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingAiWorkItems_ProjectId_OwnerId_OwnerVersion_Purpo~",
                table: "AdvertisingAiWorkItems",
                columns: new[] { "ProjectId", "OwnerId", "OwnerVersion", "Purpose", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingAttributionContexts_ProjectId_ConversationId",
                table: "AdvertisingAttributionContexts",
                columns: new[] { "ProjectId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingAttributionObservations_ProjectId_DestinationId_~",
                table: "AdvertisingAttributionObservations",
                columns: new[] { "ProjectId", "DestinationId", "MessageExternalId", "PayloadHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingAudienceSourceGrants_EnvelopeId_SourceType_Sourc~",
                table: "AdvertisingAudienceSourceGrants",
                columns: new[] { "EnvelopeId", "SourceType", "SourceExternalId", "State" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingAudienceStrategies_ProjectId_EnvelopeId_Version",
                table: "AdvertisingAudienceStrategies",
                columns: new[] { "ProjectId", "EnvelopeId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingAuditRecords_ProjectId_OccurredAtUtc_Id",
                table: "AdvertisingAuditRecords",
                columns: new[] { "ProjectId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingBudgetAllocationDebits_AllocationId_LedgerId",
                table: "AdvertisingBudgetAllocationDebits",
                columns: new[] { "AllocationId", "LedgerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingCampaignPlanCreatives_PlanId_CreativeVariantId_R~",
                table: "AdvertisingCampaignPlanCreatives",
                columns: new[] { "PlanId", "CreativeVariantId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingCampaignPlans_ProjectId_PlanHash",
                table: "AdvertisingCampaignPlans",
                columns: new[] { "ProjectId", "PlanHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingCapabilitySnapshots_ProjectId_ConnectionId_Desti~",
                table: "AdvertisingCapabilitySnapshots",
                columns: new[] { "ProjectId", "ConnectionId", "DestinationId", "CheckedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingConversionDeliveries_ProjectId_Provider_EventIde~",
                table: "AdvertisingConversionDeliveries",
                columns: new[] { "ProjectId", "Provider", "EventIdentity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingDisconnectTargets_DisconnectOperationId_TargetTy~",
                table: "AdvertisingDisconnectTargets",
                columns: new[] { "DisconnectOperationId", "TargetType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingExperimentArms_ExperimentId_IsControl",
                table: "AdvertisingExperimentArms",
                columns: new[] { "ExperimentId", "IsControl" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingKnowledgeProjections_ProjectId_DocumentId",
                table: "AdvertisingKnowledgeProjections",
                columns: new[] { "ProjectId", "DocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingManagedAdSets_ProjectId_ConnectionId_ExternalId",
                table: "AdvertisingManagedAdSets",
                columns: new[] { "ProjectId", "ConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingManagedCampaigns_ProjectId_ConnectionId_External~",
                table: "AdvertisingManagedCampaigns",
                columns: new[] { "ProjectId", "ConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingManagedOwnership_ProjectId_ProviderCampaignExter~",
                table: "AdvertisingManagedOwnership",
                columns: new[] { "ProjectId", "ProviderCampaignExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingManagedProviderCreatives_ProjectId_ConnectionId_~",
                table: "AdvertisingManagedProviderCreatives",
                columns: new[] { "ProjectId", "ConnectionId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingMediaProjections_ProjectId_AssetId",
                table: "AdvertisingMediaProjections",
                columns: new[] { "ProjectId", "AssetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingOfferDestinationGrants_EnvelopeId_OfferId_Destin~",
                table: "AdvertisingOfferDestinationGrants",
                columns: new[] { "EnvelopeId", "OfferId", "DestinationId", "State" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingProviderObjectSnapshots_OperationId_ObjectType_S~",
                table: "AdvertisingProviderObjectSnapshots",
                columns: new[] { "OperationId", "ObjectType", "SnapshotType", "StateHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingProviderOperations_ProjectId_IdempotencyKey",
                table: "AdvertisingProviderOperations",
                columns: new[] { "ProjectId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingTrackingHealthSnapshots_ProjectId_DestinationId_~",
                table: "AdvertisingTrackingHealthSnapshots",
                columns: new[] { "ProjectId", "DestinationId", "WindowStartUtc", "WindowEndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingTrackingPolicies_ProjectId_Goal_Version",
                table: "AdvertisingTrackingPolicies",
                columns: new[] { "ProjectId", "Goal", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingWhatsAppDestinations_ProjectId_ConnectionId_Phon~",
                table: "AdvertisingWhatsAppDestinations",
                columns: new[] { "ProjectId", "ConnectionId", "PhoneNumberExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingWhatsAppDestinations_Provider_WabaExternalId_Pho~",
                table: "AdvertisingWhatsAppDestinations",
                columns: new[] { "Provider", "WabaExternalId", "PhoneNumberExternalId" },
                unique: true,
                filter: "\"State\" <> 5");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAdvertisingConsentProjections_ProjectId_CustomerId",
                table: "CustomerAdvertisingConsentProjections",
                columns: new[] { "ProjectId", "CustomerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationProjectionWatermarks_ProjectId_Consumer_SourceAg~",
                table: "IntegrationProjectionWatermarks",
                columns: new[] { "ProjectId", "Consumer", "SourceAggregateType", "SourceAggregateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectAdvertisingContextProjections_ProjectId",
                table: "ProjectAdvertisingContextProjections",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundRouteProjections_ProjectId_DestinationId_Des~",
                table: "WhatsAppInboundRouteProjections",
                columns: new[] { "ProjectId", "DestinationId", "DestinationVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppInboundRouteProjections_Provider_WabaExternalId_Pho~",
                table: "WhatsAppInboundRouteProjections",
                columns: new[] { "Provider", "WabaExternalId", "PhoneNumberExternalId" },
                unique: true,
                filter: "\"State\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvertisingAiWorkItems");

            migrationBuilder.DropTable(
                name: "AdvertisingAttributionContexts");

            migrationBuilder.DropTable(
                name: "AdvertisingAttributionObservations");

            migrationBuilder.DropTable(
                name: "AdvertisingAudienceSourceGrants");

            migrationBuilder.DropTable(
                name: "AdvertisingAudienceStrategies");

            migrationBuilder.DropTable(
                name: "AdvertisingAuditRecords");

            migrationBuilder.DropTable(
                name: "AdvertisingBudgetAllocationDebits");

            migrationBuilder.DropTable(
                name: "AdvertisingCampaignPlanCreatives");

            migrationBuilder.DropTable(
                name: "AdvertisingCampaignPlans");

            migrationBuilder.DropTable(
                name: "AdvertisingCapabilitySnapshots");

            migrationBuilder.DropTable(
                name: "AdvertisingConversionDeliveries");

            migrationBuilder.DropTable(
                name: "AdvertisingDecisionImpacts");

            migrationBuilder.DropTable(
                name: "AdvertisingDisableRequests");

            migrationBuilder.DropTable(
                name: "AdvertisingDisconnectOperations");

            migrationBuilder.DropTable(
                name: "AdvertisingDisconnectTargets");

            migrationBuilder.DropTable(
                name: "AdvertisingExperimentArms");

            migrationBuilder.DropTable(
                name: "AdvertisingExperimentEvaluations");

            migrationBuilder.DropTable(
                name: "AdvertisingExperiments");

            migrationBuilder.DropTable(
                name: "AdvertisingKnowledgeProjections");

            migrationBuilder.DropTable(
                name: "AdvertisingManagedAdSets");

            migrationBuilder.DropTable(
                name: "AdvertisingManagedCampaigns");

            migrationBuilder.DropTable(
                name: "AdvertisingManagedOwnership");

            migrationBuilder.DropTable(
                name: "AdvertisingManagedProviderCreatives");

            migrationBuilder.DropTable(
                name: "AdvertisingMediaProjections");

            migrationBuilder.DropTable(
                name: "AdvertisingOfferDestinationGrants");

            migrationBuilder.DropTable(
                name: "AdvertisingProjectionBackfillRuns");

            migrationBuilder.DropTable(
                name: "AdvertisingProviderObjectSnapshots");

            migrationBuilder.DropTable(
                name: "AdvertisingProviderOperations");

            migrationBuilder.DropTable(
                name: "AdvertisingProviderValidationFindings");

            migrationBuilder.DropTable(
                name: "AdvertisingTrackingHealthSnapshots");

            migrationBuilder.DropTable(
                name: "AdvertisingTrackingPolicies");

            migrationBuilder.DropTable(
                name: "AdvertisingWhatsAppDestinations");

            migrationBuilder.DropTable(
                name: "CustomerAdvertisingConsentProjections");

            migrationBuilder.DropTable(
                name: "IntegrationProjectionWatermarks");

            migrationBuilder.DropTable(
                name: "ProjectAdvertisingContextProjections");

            migrationBuilder.DropTable(
                name: "WhatsAppInboundRouteProjections");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisingInsights_ProjectId_TargetType_TargetId_Interval~1",
                table: "AdvertisingInsights");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisingInsights_ProjectId_TargetType_TargetId_IntervalS~",
                table: "AdvertisingInsights");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisingConversionDeliveryAttempts_DeliveryId_AttemptNum~",
                table: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisingConnections_Provider_AdAccountExternalId",
                table: "AdvertisingConnections");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisingBudgetLedgers_ProjectId_EnvelopeId_PeriodKind_Pe~",
                table: "AdvertisingBudgetLedgers");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisingAttributionTouches_ProjectId_ObservationId",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.DropColumn(
                name: "AdvertisingContextVersion",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "AdSetId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "DestinationId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "DestinationType",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "EffectiveStateHash",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "ExperimentArmId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "ManagedProviderCreativeId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "OwnershipRecordId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "PlannedStateHash",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "ReconciliationState",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "FailureCode",
                table: "IntegrationInboxReceipts");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "IntegrationInboxReceipts");

            migrationBuilder.DropColumn(
                name: "ReceivedAtUtc",
                table: "IntegrationInboxReceipts");

            migrationBuilder.DropColumn(
                name: "SourceAggregateId",
                table: "IntegrationInboxReceipts");

            migrationBuilder.DropColumn(
                name: "SourceAggregateType",
                table: "IntegrationInboxReceipts");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "IntegrationInboxReceipts");

            migrationBuilder.DropColumn(
                name: "State",
                table: "IntegrationInboxReceipts");

            migrationBuilder.DropColumn(
                name: "AttributionWindowDays",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "AudienceBoundaryHash",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "DefinitionHash",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "HardCustomAudienceExclusionsJson",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "HardExcludedGeoJson",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "HardIncludedGeoJson",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "HardMinimumAge",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "HardRequiredLanguagesJson",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "PlacementPolicy",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "ReportingTimezoneIana",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "TimezoneSnapshotAtUtc",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "TimezoneSource",
                table: "AutonomyEnvelopes");

            migrationBuilder.DropColumn(
                name: "ProjectionVersion",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "OverlapEndsAtUtc",
                table: "AdvertisingWebhookSources");

            migrationBuilder.DropColumn(
                name: "ReplayEvidenceJson",
                table: "AdvertisingWebhookSources");

            migrationBuilder.DropColumn(
                name: "RevokedAtUtc",
                table: "AdvertisingWebhookSources");

            migrationBuilder.DropColumn(
                name: "RotatedAtUtc",
                table: "AdvertisingWebhookSources");

            migrationBuilder.DropColumn(
                name: "State",
                table: "AdvertisingWebhookSources");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "AdvertisingWebhookSources");

            migrationBuilder.DropColumn(
                name: "AudienceFactsJson",
                table: "AdvertisingProfiles");

            migrationBuilder.DropColumn(
                name: "ModelVersion",
                table: "AdvertisingProfiles");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "AdvertisingProfiles");

            migrationBuilder.DropColumn(
                name: "AttributionWindowDays",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "CapacityUpdatedAtUtc",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "ContributionMargin",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "CurrentCapacity",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "DailyCapacity",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "FallbackOutcomeOrderJson",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "MaximumSustainableCost",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "PolicyEvidenceJson",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "PolicyState",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "PrimaryOutcome",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "ScheduleJson",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "SpecialAdCategory",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "AdvertisingOffers");

            migrationBuilder.DropColumn(
                name: "AccountTimezone",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "AttributionSetting",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "BreakdownHash",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "FetchRunId",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "LearningStatus",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "PlacementBreakdownJson",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "ProviderActionValuesJson",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "Reach",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "SourceFreshnessUtc",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "SupersedesSnapshotId",
                table: "AdvertisingInsights");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "AdvertisingFactSources");

            migrationBuilder.DropColumn(
                name: "IsContradictory",
                table: "AdvertisingFactSources");

            migrationBuilder.DropColumn(
                name: "IsRequiredForLaunch",
                table: "AdvertisingFactSources");

            migrationBuilder.DropColumn(
                name: "NormalizedValueJson",
                table: "AdvertisingFactSources");

            migrationBuilder.DropColumn(
                name: "ObservedAtUtc",
                table: "AdvertisingFactSources");

            migrationBuilder.DropColumn(
                name: "SourceVersion",
                table: "AdvertisingFactSources");

            migrationBuilder.DropColumn(
                name: "ClaimedAtUtc",
                table: "AdvertisingExecutionCommands");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "AdvertisingExecutionCommands");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AdvertisingExecutionCommands");

            migrationBuilder.DropColumn(
                name: "ReconciledAtUtc",
                table: "AdvertisingExecutionCommands");

            migrationBuilder.DropColumn(
                name: "ReconciliationEvidenceJson",
                table: "AdvertisingExecutionCommands");

            migrationBuilder.DropColumn(
                name: "SentAtUtc",
                table: "AdvertisingExecutionCommands");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AdvertisingEmergencyStops");

            migrationBuilder.DropColumn(
                name: "ProgressJson",
                table: "AdvertisingEmergencyStops");

            migrationBuilder.DropColumn(
                name: "State",
                table: "AdvertisingEmergencyStops");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AdvertisingDecisions");

            migrationBuilder.DropColumn(
                name: "EnvelopeId",
                table: "AdvertisingDecisions");

            migrationBuilder.DropColumn(
                name: "EvidenceHash",
                table: "AdvertisingDecisions");

            migrationBuilder.DropColumn(
                name: "ExecutionCommandId",
                table: "AdvertisingDecisions");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "AdvertisingDecisions");

            migrationBuilder.DropColumn(
                name: "ReasonCodesJson",
                table: "AdvertisingDecisions");

            migrationBuilder.DropColumn(
                name: "ModelVersion",
                table: "AdvertisingDecisionReviews");

            migrationBuilder.DropColumn(
                name: "PromptVersion",
                table: "AdvertisingDecisionReviews");

            migrationBuilder.DropColumn(
                name: "ReviewedAtUtc",
                table: "AdvertisingDecisionReviews");

            migrationBuilder.DropColumn(
                name: "BucketEndUtc",
                table: "AdvertisingCycleRuns");

            migrationBuilder.DropColumn(
                name: "LeaseOwner",
                table: "AdvertisingCycleRuns");

            migrationBuilder.DropColumn(
                name: "ReportingTimezoneIana",
                table: "AdvertisingCycleRuns");

            migrationBuilder.DropColumn(
                name: "CallToAction",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "ContentHash",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "GeneratedAtUtc",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "Headline",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "OfferFactHash",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "PageCompatibilityJson",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "PlacementFormat",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "PrimaryText",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "ThumbnailObjectKey",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "WhatsAppDestinationCompatibilityJson",
                table: "AdvertisingCreativeVariants");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "AdvertisingCreatives");

            migrationBuilder.DropColumn(
                name: "ConceptKey",
                table: "AdvertisingCreatives");

            migrationBuilder.DropColumn(
                name: "HookKey",
                table: "AdvertisingCreatives");

            migrationBuilder.DropColumn(
                name: "OrganicEvidenceJson",
                table: "AdvertisingCreatives");

            migrationBuilder.DropColumn(
                name: "PaidEvidenceJson",
                table: "AdvertisingCreatives");

            migrationBuilder.DropColumn(
                name: "RecommendationBand",
                table: "AdvertisingCreatives");

            migrationBuilder.DropColumn(
                name: "BusinessAggregateId",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "BusinessAggregateType",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "ConsentEvidenceJson",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "EventType",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "JourneyLocation",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "NormalizedPayloadJson",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "OccurredAtUtc",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropColumn(
                name: "AttributionState",
                table: "AdvertisingConversions");

            migrationBuilder.DropColumn(
                name: "AttributionTouchId",
                table: "AdvertisingConversions");

            migrationBuilder.DropColumn(
                name: "AttributionWindowEndsAtUtc",
                table: "AdvertisingConversions");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AdvertisingConversions");

            migrationBuilder.DropColumn(
                name: "CorrectionState",
                table: "AdvertisingConversions");

            migrationBuilder.DropColumn(
                name: "SourceHistoryJson",
                table: "AdvertisingConversions");

            migrationBuilder.DropColumn(
                name: "TruthState",
                table: "AdvertisingConversions");

            migrationBuilder.DropColumn(
                name: "DeliveryId",
                table: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "EventsReceived",
                table: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                table: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "ProviderRequestId",
                table: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "ProviderTraceId",
                table: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "ResponseHash",
                table: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "WarningsJson",
                table: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropColumn(
                name: "SourceEventId",
                table: "AdvertisingConversionAdjustments");

            migrationBuilder.DropColumn(
                name: "AccountStatus",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "AccountTimezoneIana",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "FundingStatus",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "GrantedPermissionsJson",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "GraphApiVersion",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "LastProviderTraceId",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "ReferralProofAtUtc",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "ReferralProofHash",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "ReferralProofState",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "TimezoneSource",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "TimezoneValidatedAtUtc",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "WabaExternalId",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "WhatsAppIntegrationMode",
                table: "AdvertisingConnections");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AdvertisingBudgetLedgers");

            migrationBuilder.DropColumn(
                name: "DelayedSpendEstimate",
                table: "AdvertisingBudgetLedgers");

            migrationBuilder.DropColumn(
                name: "EnvelopeVersion",
                table: "AdvertisingBudgetLedgers");

            migrationBuilder.DropColumn(
                name: "ForecastSpend",
                table: "AdvertisingBudgetLedgers");

            migrationBuilder.DropColumn(
                name: "PeriodKind",
                table: "AdvertisingBudgetLedgers");

            migrationBuilder.DropColumn(
                name: "ConcurrencyToken",
                table: "AdvertisingBudgetAllocations");

            migrationBuilder.DropColumn(
                name: "ExperimentId",
                table: "AdvertisingBudgetAllocations");

            migrationBuilder.DropColumn(
                name: "ExternalBudgetOwnerId",
                table: "AdvertisingBudgetAllocations");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "AdvertisingBudgetAllocations");

            migrationBuilder.DropColumn(
                name: "TargetType",
                table: "AdvertisingBudgetAllocations");

            migrationBuilder.DropColumn(
                name: "AttributionContextId",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.DropColumn(
                name: "ConversationId",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.DropColumn(
                name: "DestinationId",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.DropColumn(
                name: "EligibilityEvidenceJson",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.DropColumn(
                name: "JourneyKey",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.DropColumn(
                name: "ObservationId",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.DropColumn(
                name: "ProtectedCtwaClid",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.DropColumn(
                name: "ProviderAdExternalId",
                table: "AdvertisingAttributionTouches");

            migrationBuilder.AlterColumn<DateTime>(
                name: "ProcessedAtUtc",
                table: "IntegrationInboxReceipts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingInsights_ProjectId_TargetId_IntervalStartUtc_Int~",
                table: "AdvertisingInsights",
                columns: new[] { "ProjectId", "TargetId", "IntervalStartUtc", "IntervalEndUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingBudgetLedgers_ProjectId_EnvelopeId_PeriodStartUtc",
                table: "AdvertisingBudgetLedgers",
                columns: new[] { "ProjectId", "EnvelopeId", "PeriodStartUtc" },
                unique: true);
        }
    }
}
