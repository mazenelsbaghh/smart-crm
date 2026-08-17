using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookAdsManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdvertisingAttributionTouches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdvertisementId = table.Column<Guid>(type: "uuid", nullable: true),
                    Method = table.Column<string>(type: "text", nullable: false),
                    ExternalClickIdHash = table.Column<string>(type: "text", nullable: true),
                    TouchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingAttributionTouches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingBudgetAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    AllocatedAmount = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingBudgetAllocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingBudgetLedgers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvelopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PeriodEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AuthorizedCap = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SafetyReserve = table.Column<decimal>(type: "numeric", nullable: false),
                    UsableCap = table.Column<decimal>(type: "numeric", nullable: false),
                    CommittedAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    ObservedSpend = table.Column<decimal>(type: "numeric", nullable: false),
                    ReleasedAmount = table.Column<decimal>(type: "numeric", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    LastReconciledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingBudgetLedgers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    AdAccountExternalId = table.Column<string>(type: "text", nullable: true),
                    PageExternalId = table.Column<string>(type: "text", nullable: true),
                    DatasetExternalId = table.Column<string>(type: "text", nullable: true),
                    ProtectedAccessToken = table.Column<string>(type: "text", nullable: true),
                    GrantedCapabilitiesJson = table.Column<string>(type: "text", nullable: false),
                    AccountCurrency = table.Column<string>(type: "text", nullable: true),
                    AccountTimezone = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastValidatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSyncAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastErrorSummary = table.Column<string>(type: "text", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingConversionAdjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalEventId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    ValueDelta = table.Column<decimal>(type: "numeric", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingConversionAdjustments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingConversionDeliveryAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingConversionDeliveryAttempts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingConversions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalKey = table.Column<string>(type: "text", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CustomerReference = table.Column<string>(type: "text", nullable: true),
                    VisitorReference = table.Column<string>(type: "text", nullable: true),
                    Value = table.Column<decimal>(type: "numeric", nullable: true),
                    CurrentValue = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    AdvertisementId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreativeId = table.Column<Guid>(type: "uuid", nullable: true),
                    AttributionMethod = table.Column<string>(type: "text", nullable: false),
                    ConsentState = table.Column<int>(type: "integer", nullable: false),
                    LegalBasis = table.Column<string>(type: "text", nullable: true),
                    ProtectedMatchData = table.Column<string>(type: "text", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingConversions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingConversionSourceEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceSystem = table.Column<string>(type: "text", nullable: false),
                    ExternalEventId = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    PayloadHash = table.Column<string>(type: "text", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessingState = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingConversionSourceEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingCreatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<int>(type: "integer", nullable: false),
                    SourceExternalId = table.Column<string>(type: "text", nullable: true),
                    SourceAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceStoragePath = table.Column<string>(type: "text", nullable: true),
                    SourceContentType = table.Column<string>(type: "text", nullable: true),
                    SourceHash = table.Column<string>(type: "text", nullable: false),
                    SourceVersion = table.Column<int>(type: "integer", nullable: false),
                    MediaType = table.Column<int>(type: "integer", nullable: false),
                    RightsState = table.Column<string>(type: "text", nullable: false),
                    PolicyState = table.Column<string>(type: "text", nullable: false),
                    EligibilityState = table.Column<int>(type: "integer", nullable: false),
                    RecommendationScore = table.Column<decimal>(type: "numeric", nullable: false),
                    RecommendationEvidenceJson = table.Column<string>(type: "text", nullable: false),
                    FatigueState = table.Column<string>(type: "text", nullable: false),
                    LastAnalyzedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingCreatives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingCreativeVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Placement = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    StoragePath = table.Column<string>(type: "text", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    SourceHash = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingCreativeVariants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingCycleRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobName = table.Column<string>(type: "text", nullable: false),
                    BucketStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorType = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingCycleRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingDecisionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerType = table.Column<string>(type: "text", nullable: false),
                    Verdict = table.Column<int>(type: "integer", nullable: false),
                    ReasonsJson = table.Column<string>(type: "text", nullable: false),
                    EvidenceHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingDecisionReviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionType = table.Column<string>(type: "text", nullable: false),
                    TargetType = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: true),
                    EvidenceStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    ProposedChangeJson = table.Column<string>(type: "text", nullable: false),
                    RiskClass = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    EvaluateAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingDecisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingEmergencyStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Trigger = table.Column<int>(type: "integer", nullable: false),
                    ActivatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResumedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingEmergencyStops", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingExecutionCommands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    CommandType = table.Column<string>(type: "text", nullable: false),
                    TargetExternalId = table.Column<string>(type: "text", nullable: true),
                    ExpectedStateHash = table.Column<string>(type: "text", nullable: true),
                    DesiredStateJson = table.Column<string>(type: "text", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ProviderRequestId = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingExecutionCommands", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingFactSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: true),
                    FactName = table.Column<string>(type: "text", nullable: false),
                    FactValue = table.Column<string>(type: "text", nullable: false),
                    KnowledgeDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeVersion = table.Column<int>(type: "integer", nullable: false),
                    Citation = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingFactSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingInsights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetType = table.Column<string>(type: "text", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    IntervalStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IntervalEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Spend = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Impressions = table.Column<long>(type: "bigint", nullable: false),
                    Clicks = table.Column<long>(type: "bigint", nullable: false),
                    Frequency = table.Column<decimal>(type: "numeric", nullable: false),
                    ProviderActionsJson = table.Column<string>(type: "text", nullable: false),
                    FetchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingInsights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric", nullable: true),
                    Currency = table.Column<string>(type: "text", nullable: true),
                    DestinationsJson = table.Column<string>(type: "text", nullable: false),
                    MarketsJson = table.Column<string>(type: "text", nullable: false),
                    AllowedClaimsJson = table.Column<string>(type: "text", nullable: false),
                    RestrictionsJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingOffers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    KnowledgeRevisionHash = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    OfferType = table.Column<string>(type: "text", nullable: true),
                    FunnelJson = table.Column<string>(type: "text", nullable: false),
                    AudienceJson = table.Column<string>(type: "text", nullable: false),
                    BrandRulesJson = table.Column<string>(type: "text", nullable: false),
                    ProhibitedClaimsJson = table.Column<string>(type: "text", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StaleAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingPromotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnvelopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Objective = table.Column<string>(type: "text", nullable: false),
                    DestinationType = table.Column<string>(type: "text", nullable: false),
                    DestinationUrl = table.Column<string>(type: "text", nullable: false),
                    OptimizationEvent = table.Column<string>(type: "text", nullable: false),
                    FunnelJson = table.Column<string>(type: "text", nullable: false),
                    AudiencePlanJson = table.Column<string>(type: "text", nullable: false),
                    AllocationPlanJson = table.Column<string>(type: "text", nullable: false),
                    ReadinessJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PausedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingPromotions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdvertisingWebhookSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    ProtectedSigningSecret = table.Column<string>(type: "text", nullable: false),
                    AllowedEventTypesJson = table.Column<string>(type: "text", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdvertisingWebhookSources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutonomyEnvelopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: true),
                    DailyCap = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    PeriodCap = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    PeriodCapKind = table.Column<string>(type: "text", nullable: false),
                    Currency = table.Column<string>(type: "text", nullable: false),
                    SafetyReservePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    MaximumIncreasePercent = table.Column<decimal>(type: "numeric", nullable: false),
                    CooldownHours = table.Column<int>(type: "integer", nullable: false),
                    AllowedCountriesJson = table.Column<string>(type: "text", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    AuthorizedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutonomyEnvelopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationInboxReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "text", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationInboxReceipts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationOutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationOutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedAdvertisements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CampaignExternalId = table.Column<string>(type: "text", nullable: true),
                    AdSetExternalId = table.Column<string>(type: "text", nullable: true),
                    AdExternalId = table.Column<string>(type: "text", nullable: true),
                    PublisherPlatform = table.Column<string>(type: "text", nullable: false),
                    PositionsJson = table.Column<string>(type: "text", nullable: false),
                    DailyBudget = table.Column<decimal>(type: "numeric", nullable: false),
                    ConfiguredStatus = table.Column<int>(type: "integer", nullable: false),
                    EffectiveStatus = table.Column<string>(type: "text", nullable: false),
                    ProviderStateHash = table.Column<string>(type: "text", nullable: true),
                    LastSyncedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedAdvertisements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackingIncidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Severity = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    DetectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecoveredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackingIncidents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingBudgetLedgers_ProjectId_EnvelopeId_PeriodStartUtc",
                table: "AdvertisingBudgetLedgers",
                columns: new[] { "ProjectId", "EnvelopeId", "PeriodStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingConnections_ProjectId_Provider",
                table: "AdvertisingConnections",
                columns: new[] { "ProjectId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingConversionAdjustments_ProjectId_ExternalEventId",
                table: "AdvertisingConversionAdjustments",
                columns: new[] { "ProjectId", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingConversions_ProjectId_CanonicalKey",
                table: "AdvertisingConversions",
                columns: new[] { "ProjectId", "CanonicalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingConversionSourceEvents_ProjectId_SourceSystem_Ex~",
                table: "AdvertisingConversionSourceEvents",
                columns: new[] { "ProjectId", "SourceSystem", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingCreativeVariants_ProjectId_CreativeId_Placement_~",
                table: "AdvertisingCreativeVariants",
                columns: new[] { "ProjectId", "CreativeId", "Placement", "SourceHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingCycleRuns_ProjectId_JobName_BucketStartUtc",
                table: "AdvertisingCycleRuns",
                columns: new[] { "ProjectId", "JobName", "BucketStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingExecutionCommands_ProjectId_IdempotencyKey",
                table: "AdvertisingExecutionCommands",
                columns: new[] { "ProjectId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingFactSources_ProjectId_ProfileId_FactName",
                table: "AdvertisingFactSources",
                columns: new[] { "ProjectId", "ProfileId", "FactName" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingInsights_ProjectId_TargetId_IntervalStartUtc_Int~",
                table: "AdvertisingInsights",
                columns: new[] { "ProjectId", "TargetId", "IntervalStartUtc", "IntervalEndUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingWebhookSources_ProjectId_SourceKey",
                table: "AdvertisingWebhookSources",
                columns: new[] { "ProjectId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AutonomyEnvelopes_ProjectId_State",
                table: "AutonomyEnvelopes",
                columns: new[] { "ProjectId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationInboxReceipts_EventId_Consumer",
                table: "IntegrationInboxReceipts",
                columns: new[] { "EventId", "Consumer" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxMessages_EventId",
                table: "IntegrationOutboxMessages",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationOutboxMessages_PublishedAtUtc_NextAttemptAtUtc",
                table: "IntegrationOutboxMessages",
                columns: new[] { "PublishedAtUtc", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdvertisingAttributionTouches");

            migrationBuilder.DropTable(
                name: "AdvertisingBudgetAllocations");

            migrationBuilder.DropTable(
                name: "AdvertisingBudgetLedgers");

            migrationBuilder.DropTable(
                name: "AdvertisingConnections");

            migrationBuilder.DropTable(
                name: "AdvertisingConversionAdjustments");

            migrationBuilder.DropTable(
                name: "AdvertisingConversionDeliveryAttempts");

            migrationBuilder.DropTable(
                name: "AdvertisingConversions");

            migrationBuilder.DropTable(
                name: "AdvertisingConversionSourceEvents");

            migrationBuilder.DropTable(
                name: "AdvertisingCreatives");

            migrationBuilder.DropTable(
                name: "AdvertisingCreativeVariants");

            migrationBuilder.DropTable(
                name: "AdvertisingCycleRuns");

            migrationBuilder.DropTable(
                name: "AdvertisingDecisionReviews");

            migrationBuilder.DropTable(
                name: "AdvertisingDecisions");

            migrationBuilder.DropTable(
                name: "AdvertisingEmergencyStops");

            migrationBuilder.DropTable(
                name: "AdvertisingExecutionCommands");

            migrationBuilder.DropTable(
                name: "AdvertisingFactSources");

            migrationBuilder.DropTable(
                name: "AdvertisingInsights");

            migrationBuilder.DropTable(
                name: "AdvertisingOffers");

            migrationBuilder.DropTable(
                name: "AdvertisingProfiles");

            migrationBuilder.DropTable(
                name: "AdvertisingPromotions");

            migrationBuilder.DropTable(
                name: "AdvertisingWebhookSources");

            migrationBuilder.DropTable(
                name: "AutonomyEnvelopes");

            migrationBuilder.DropTable(
                name: "IntegrationInboxReceipts");

            migrationBuilder.DropTable(
                name: "IntegrationOutboxMessages");

            migrationBuilder.DropTable(
                name: "ManagedAdvertisements");

            migrationBuilder.DropTable(
                name: "TrackingIncidents");
        }
    }
}
