using Microsoft.EntityFrameworkCore;
using Shared.Domain;
using Shared.Security;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;

namespace Shared.Infrastructure
{
    public class AppDbContext : DbContext
    {
        private readonly ITenantContext _tenantContext;
        private readonly IServiceProvider _serviceProvider;
        private int _entityIndexEventSuppressionDepth;

        public DbSet<Modules.Auth.Domain.User> Users { get; set; }
        public DbSet<Modules.Auth.Domain.RefreshToken> RefreshTokens { get; set; }
        public DbSet<Modules.Projects.Domain.Project> Projects { get; set; }
        public DbSet<Modules.Projects.Domain.ProjectSettings> ProjectSettings { get; set; }
        public DbSet<Modules.Conversations.Domain.Customer> Customers { get; set; }
        public DbSet<Modules.Conversations.Domain.Conversation> Conversations { get; set; }
        public DbSet<Modules.Conversations.Domain.Message> Messages { get; set; }
        public DbSet<Modules.Conversations.Domain.ConversationReplyWindow> ConversationReplyWindows { get; set; }
        public DbSet<Modules.WhatsApp.Domain.WhatsAppAccount> WhatsAppAccounts { get; set; }
        public DbSet<Modules.WhatsApp.Domain.WhatsAppCustomerIdentity> WhatsAppCustomerIdentities { get; set; }
        public DbSet<Modules.WhatsApp.Domain.WhatsAppPhoneCustomerIdentity> WhatsAppPhoneCustomerIdentities { get; set; }
        public DbSet<Modules.CRM.Domain.FollowUp> FollowUps { get; set; }
        public DbSet<Modules.CRM.Domain.CustomerTask> CustomerTasks { get; set; }
        public DbSet<Modules.CRM.Domain.CRMUpdateProposal> CRMUpdateProposals { get; set; }
        public DbSet<Modules.Conversations.Domain.NotificationAlert> NotificationAlerts { get; set; }
        public DbSet<Modules.TalkTips.Domain.TrialReminder> TalkTipsTrialReminders { get; set; }
        public DbSet<Modules.Brain.Domain.KnowledgeDocument> KnowledgeDocuments { get; set; }
        public DbSet<Modules.Brain.Domain.KnowledgeChunk> KnowledgeChunks { get; set; }
        public DbSet<Modules.Workflows.Domain.AutomationWorkflow> AutomationWorkflows { get; set; }
        public DbSet<Modules.Workflows.Domain.WorkflowExecutionLog> WorkflowExecutionLogs { get; set; }
        public DbSet<Modules.Approvals.Domain.ApprovalRequest> ApprovalRequests { get; set; }
        public DbSet<Modules.Integrations.Domain.ProjectIntegration> ProjectIntegrations { get; set; }
        public DbSet<Modules.Customers.Domain.CustomerMemory> CustomerMemories { get; set; }
        public DbSet<Modules.CRM.Domain.Segment> Segments { get; set; }
        public DbSet<Modules.CRM.Domain.PipelineStage> PipelineStages { get; set; }
        public DbSet<Modules.CRM.Domain.Deal> Deals { get; set; }
        public DbSet<Modules.Campaigns.Domain.Campaign> Campaigns { get; set; }
        public DbSet<Modules.Campaigns.Domain.CampaignRecipient> CampaignRecipients { get; set; }
        public DbSet<Modules.Analytics.Domain.AnalyticsSnapshot> AnalyticsSnapshots { get; set; }
        public DbSet<Modules.Analytics.Domain.ConversationSalesAnalysis> ConversationSalesAnalyses { get; set; }
        public DbSet<Modules.Analytics.Domain.SalesIntelligenceDigest> SalesIntelligenceDigests { get; set; }
        public DbSet<Modules.Media.Domain.Asset> Assets { get; set; }
        public DbSet<Modules.Media.Domain.AssetVariant> AssetVariants { get; set; }
        public DbSet<Modules.Audit.Domain.AuditLog> AuditLogs { get; set; }
        public DbSet<Modules.GroupAppointments.Domain.GroupAppointment> GroupAppointments { get; set; }
        public DbSet<Modules.GroupAppointments.Domain.GroupAppointmentBooking> GroupAppointmentBookings { get; set; }
        public DbSet<Modules.Facebook.Domain.ConnectedPage> ConnectedPages { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingConnection> AdvertisingConnections { get; set; }
        public DbSet<Modules.Advertising.Domain.AuthorizedWhatsAppDestination> AdvertisingWhatsAppDestinations { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingCapabilitySnapshot> AdvertisingCapabilitySnapshots { get; set; }
        public DbSet<Modules.Advertising.Domain.ConnectionDisconnectOperation> AdvertisingDisconnectOperations { get; set; }
        public DbSet<Modules.Advertising.Domain.ConnectionDisconnectTarget> AdvertisingDisconnectTargets { get; set; }
        public DbSet<Modules.Advertising.Domain.AutonomyEnvelope> AutonomyEnvelopes { get; set; }
        public DbSet<Modules.Advertising.Domain.EnvelopeOfferDestinationGrant> AdvertisingOfferDestinationGrants { get; set; }
        public DbSet<Modules.Advertising.Domain.EnvelopeAudienceSourceGrant> AdvertisingAudienceSourceGrants { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingProfile> AdvertisingProfiles { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingOffer> AdvertisingOffers { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingFactSource> AdvertisingFactSources { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingPromotion> AdvertisingPromotions { get; set; }
        public DbSet<Modules.Advertising.Domain.CampaignPlan> AdvertisingCampaignPlans { get; set; }
        public DbSet<Modules.Advertising.Domain.AudienceStrategy> AdvertisingAudienceStrategies { get; set; }
        public DbSet<Modules.Advertising.Domain.CampaignPlanCreative> AdvertisingCampaignPlanCreatives { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingExperiment> AdvertisingExperiments { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingExperimentArm> AdvertisingExperimentArms { get; set; }
        public DbSet<Modules.Advertising.Domain.ExperimentEvaluation> AdvertisingExperimentEvaluations { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingCreative> AdvertisingCreatives { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingCreativeVariant> AdvertisingCreativeVariants { get; set; }
        public DbSet<Modules.Advertising.Domain.ManagedAdvertisement> ManagedAdvertisements { get; set; }
        public DbSet<Modules.Advertising.Domain.ManagedOwnershipRecord> AdvertisingManagedOwnership { get; set; }
        public DbSet<Modules.Advertising.Domain.ManagedCampaign> AdvertisingManagedCampaigns { get; set; }
        public DbSet<Modules.Advertising.Domain.ManagedAdSet> AdvertisingManagedAdSets { get; set; }
        public DbSet<Modules.Advertising.Domain.ManagedProviderCreative> AdvertisingManagedProviderCreatives { get; set; }
        public DbSet<Modules.Advertising.Domain.ProviderOperation> AdvertisingProviderOperations { get; set; }
        public DbSet<Modules.Advertising.Domain.ProviderObjectSnapshot> AdvertisingProviderObjectSnapshots { get; set; }
        public DbSet<Modules.Advertising.Domain.ProviderValidationFinding> AdvertisingProviderValidationFindings { get; set; }
        public DbSet<Modules.Advertising.Domain.BudgetPeriodLedger> AdvertisingBudgetLedgers { get; set; }
        public DbSet<Modules.Advertising.Domain.BudgetAllocation> AdvertisingBudgetAllocations { get; set; }
        public DbSet<Modules.Advertising.Domain.BudgetAllocationLedgerDebit> AdvertisingBudgetAllocationDebits { get; set; }
        public DbSet<Modules.Advertising.Domain.InsightsSnapshot> AdvertisingInsights { get; set; }
        public DbSet<Modules.Advertising.Domain.ConversionSourceEvent> AdvertisingConversionSourceEvents { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingWebhookSource> AdvertisingWebhookSources { get; set; }
        public DbSet<Modules.Advertising.Domain.CanonicalConversion> AdvertisingConversions { get; set; }
        public DbSet<Modules.Advertising.Domain.ConversionAdjustment> AdvertisingConversionAdjustments { get; set; }
        public DbSet<Modules.Advertising.Domain.WhatsAppAttributionObservation> AdvertisingAttributionObservations { get; set; }
        public DbSet<Modules.Advertising.Domain.WhatsAppAttributionContext> AdvertisingAttributionContexts { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingAttributionTouch> AdvertisingAttributionTouches { get; set; }
        public DbSet<Modules.Advertising.Domain.ConversionDelivery> AdvertisingConversionDeliveries { get; set; }
        public DbSet<Modules.Advertising.Domain.ConversionDeliveryAttempt> AdvertisingConversionDeliveryAttempts { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingDecision> AdvertisingDecisions { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingAiWorkItem> AdvertisingAiWorkItems { get; set; }
        public DbSet<Modules.Advertising.Domain.DecisionReview> AdvertisingDecisionReviews { get; set; }
        public DbSet<Modules.Advertising.Domain.DecisionImpact> AdvertisingDecisionImpacts { get; set; }
        public DbSet<Modules.Advertising.Domain.ExecutionCommand> AdvertisingExecutionCommands { get; set; }
        public DbSet<Modules.Advertising.Domain.TrackingIncident> TrackingIncidents { get; set; }
        public DbSet<Modules.Advertising.Domain.EmergencyStopRecord> AdvertisingEmergencyStops { get; set; }
        public DbSet<Modules.Advertising.Domain.AutopilotDisableRequest> AdvertisingDisableRequests { get; set; }
        public DbSet<Modules.Advertising.Domain.TrackingHealthPolicy> AdvertisingTrackingPolicies { get; set; }
        public DbSet<Modules.Advertising.Domain.TrackingHealthSnapshot> AdvertisingTrackingHealthSnapshots { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingAuditRecord> AdvertisingAuditRecords { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingCycleRun> AdvertisingCycleRuns { get; set; }
        public DbSet<IntegrationOutboxMessage> IntegrationOutboxMessages { get; set; }
        public DbSet<IntegrationInboxReceipt> IntegrationInboxReceipts { get; set; }
        public DbSet<IntegrationProjectionWatermark> IntegrationProjectionWatermarks { get; set; }
        public DbSet<Modules.Advertising.Domain.ProjectAdvertisingContextProjection> ProjectAdvertisingContextProjections { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingKnowledgeProjection> AdvertisingKnowledgeProjections { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingMediaProjection> AdvertisingMediaProjections { get; set; }
        public DbSet<Modules.Advertising.Domain.CustomerAdvertisingConsentProjection> CustomerAdvertisingConsentProjections { get; set; }
        public DbSet<Modules.Advertising.Domain.AdvertisingProjectionBackfillRun> AdvertisingProjectionBackfillRuns { get; set; }
        public DbSet<Modules.WhatsApp.Domain.WhatsAppInboundRouteProjection> WhatsAppInboundRouteProjections { get; set; }

        public DbSet<Modules.QuranChallenge.Domain.QuranYouTubeSettings> QuranYouTubeSettings { get; set; }
        public DbSet<Modules.QuranChallenge.Domain.QuranFacebookSettings> QuranFacebookSettings { get; set; }
        public DbSet<Modules.QuranChallenge.Domain.QuranTikTokSettings> QuranTikTokSettings { get; set; }
        public DbSet<Modules.Content.Domain.ContentAutomationSettings> ContentAutomationSettings { get; set; }
        public DbSet<Modules.Content.Domain.ContentPost> ContentPosts { get; set; }
        public DbSet<Modules.Content.Domain.ContentWeekPlan> ContentWeekPlans { get; set; }
        public DbSet<Modules.Content.Domain.ContentWeekPlanItem> ContentWeekPlanItems { get; set; }
        public DbSet<Modules.Content.Domain.ContentVideo> ContentVideos { get; set; }
        public DbSet<Modules.Content.Domain.ContentVideoScene> ContentVideoScenes { get; set; }

        public Guid CurrentProjectId => _tenantContext.ProjectId;

        public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext, IServiceProvider serviceProvider)
            : base(options)
        {
            _tenantContext = tenantContext;
            _serviceProvider = serviceProvider;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
            {
                modelBuilder.Entity<Modules.Brain.Domain.KnowledgeChunk>().Ignore(c => c.Embedding);
            }
            else
            {
                modelBuilder.HasPostgresExtension("vector");
                modelBuilder.Entity<Modules.Brain.Domain.KnowledgeChunk>()
                    .Property(c => c.Embedding)
                    .HasColumnType("vector(768)");
            }

            modelBuilder.Entity<Modules.Conversations.Domain.Message>()
                .HasOne<Modules.Media.Domain.Asset>()
                .WithMany()
                .HasForeignKey(m => m.AssetId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Modules.Conversations.Domain.Conversation>()
                .HasIndex(conversation => new { conversation.Status, conversation.LastMessageTimestamp });

            modelBuilder.Entity<Modules.Conversations.Domain.Conversation>()
                .HasIndex(conversation => new
                {
                    conversation.ProjectId,
                    conversation.CustomerId,
                    conversation.Channel,
                    conversation.WhatsAppAccountId,
                    conversation.Status
                });

            modelBuilder.Entity<Modules.Conversations.Domain.Conversation>()
                .HasIndex(conversation => new { conversation.ProjectId, conversation.WhatsAppDestinationId });

            modelBuilder.Entity<Modules.Conversations.Domain.Conversation>()
                .HasOne<Modules.WhatsApp.Domain.WhatsAppAccount>()
                .WithMany()
                .HasForeignKey(conversation => new
                {
                    conversation.WhatsAppAccountId,
                    conversation.ProjectId
                })
                .HasPrincipalKey(account => new { account.Id, account.ProjectId })
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Modules.Conversations.Domain.Message>()
                .HasIndex(message => new { message.ConversationId, message.Timestamp, message.Id })
                .IsDescending(false, true, true);

            var replyWindowEntity = modelBuilder.Entity<Modules.Conversations.Domain.ConversationReplyWindow>();
            replyWindowEntity.Property(window => window.Channel).HasMaxLength(32).IsRequired();
            replyWindowEntity.Property(window => window.Sender).HasMaxLength(180).IsRequired();
            replyWindowEntity.Property(window => window.WhatsAppDeliveryIdempotencyKey).HasMaxLength(220);
            replyWindowEntity.HasIndex(window => window.ConversationId).IsUnique();
            replyWindowEntity.HasIndex(window => new { window.DueAtUtc, window.DispatchedEventId });
            replyWindowEntity.HasOne<Modules.Conversations.Domain.Conversation>()
                .WithMany()
                .HasForeignKey(window => window.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
            replyWindowEntity.HasOne<Modules.Conversations.Domain.Message>()
                .WithMany()
                .HasForeignKey(window => window.LatestIncomingMessageId)
                .OnDelete(DeleteBehavior.Restrict);
            replyWindowEntity.HasOne<Modules.WhatsApp.Domain.WhatsAppAccount>()
                .WithMany()
                .HasForeignKey(window => new { window.WhatsAppAccountId, window.ProjectId })
                .HasPrincipalKey(account => new { account.Id, account.ProjectId })
                .OnDelete(DeleteBehavior.Restrict);

            var salesAnalysisEntity = modelBuilder.Entity<Modules.Analytics.Domain.ConversationSalesAnalysis>();
            salesAnalysisEntity.HasIndex(analysis => new { analysis.ProjectId, analysis.ConversationId }).IsUnique();
            salesAnalysisEntity.HasIndex(analysis => new { analysis.ProjectId, analysis.ConversationStartedAtUtc });
            salesAnalysisEntity.HasIndex(analysis => new { analysis.ProjectId, analysis.NeedsFollowUp, analysis.FollowUpPriority });
            salesAnalysisEntity.Property(analysis => analysis.Confidence).HasPrecision(5, 4);

            var salesDigestEntity = modelBuilder.Entity<Modules.Analytics.Domain.SalesIntelligenceDigest>();
            salesDigestEntity.HasIndex(digest => new { digest.ProjectId, digest.WindowStartUtc, digest.WindowEndUtc });
            salesDigestEntity.HasIndex(digest => new { digest.ProjectId, digest.GeneratedAtUtc });

            modelBuilder.Entity<Modules.CRM.Domain.FollowUp>()
                .HasIndex(followUp => followUp.ConversationId);

            modelBuilder.Entity<Modules.CRM.Domain.FollowUp>()
                .Property(followUp => followUp.ActiveAutomationSlotKey)
                .HasMaxLength(220);

            modelBuilder.Entity<Modules.CRM.Domain.FollowUp>()
                .HasIndex(followUp => new { followUp.ProjectId, followUp.ActiveAutomationSlotKey })
                .IsUnique()
                .HasFilter("\"ActiveAutomationSlotKey\" IS NOT NULL AND \"Status\" IN ('Pending', 'Processing')");

            modelBuilder.Entity<Modules.CRM.Domain.FollowUp>()
                .HasOne<Modules.WhatsApp.Domain.WhatsAppAccount>()
                .WithMany()
                .HasForeignKey(followUp => new
                {
                    followUp.WhatsAppAccountId,
                    followUp.ProjectId
                })
                .HasPrincipalKey(account => new { account.Id, account.ProjectId })
                .OnDelete(DeleteBehavior.Restrict);

            var whatsAppAccountEntity = modelBuilder.Entity<Modules.WhatsApp.Domain.WhatsAppAccount>();
            whatsAppAccountEntity.Property(account => account.Name).HasMaxLength(80).IsRequired();
            whatsAppAccountEntity.HasAlternateKey(account => new { account.Id, account.ProjectId });
            whatsAppAccountEntity.HasIndex(account => new { account.ProjectId, account.IsDefault })
                .IsUnique()
                .HasFilter("\"IsDefault\" = TRUE");

            var whatsAppIdentityEntity = modelBuilder.Entity<Modules.WhatsApp.Domain.WhatsAppCustomerIdentity>();
            whatsAppIdentityEntity.Property(identity => identity.ExternalId).HasMaxLength(160).IsRequired();
            whatsAppIdentityEntity.Property(identity => identity.Kind).HasMaxLength(20).IsRequired();
            whatsAppIdentityEntity.HasIndex(identity => new { identity.WhatsAppAccountId, identity.ExternalId }).IsUnique();
            whatsAppIdentityEntity.HasOne<Modules.WhatsApp.Domain.WhatsAppAccount>()
                .WithMany()
                .HasForeignKey(identity => new { identity.WhatsAppAccountId, identity.ProjectId })
                .HasPrincipalKey(account => new { account.Id, account.ProjectId })
                .OnDelete(DeleteBehavior.Cascade);
            whatsAppIdentityEntity.HasOne<Modules.Conversations.Domain.Customer>()
                .WithMany()
                .HasForeignKey(identity => new { identity.CustomerId, identity.ProjectId })
                .HasPrincipalKey(customer => new { customer.Id, customer.ProjectId })
                .OnDelete(DeleteBehavior.Cascade);

            var whatsAppPhoneIdentityEntity = modelBuilder.Entity<Modules.WhatsApp.Domain.WhatsAppPhoneCustomerIdentity>();
            whatsAppPhoneIdentityEntity.Property(identity => identity.NormalizedPhone).HasMaxLength(32).IsRequired();
            whatsAppPhoneIdentityEntity.HasIndex(identity => new
            {
                identity.ProjectId,
                identity.NormalizedPhone
            }).IsUnique();
            whatsAppPhoneIdentityEntity.HasOne<Modules.Conversations.Domain.Customer>()
                .WithMany()
                .HasForeignKey(identity => new { identity.CustomerId, identity.ProjectId })
                .HasPrincipalKey(customer => new { customer.Id, customer.ProjectId })
                .OnDelete(DeleteBehavior.Cascade);

            var customerEntity = modelBuilder.Entity<Modules.Conversations.Domain.Customer>();
            customerEntity.HasAlternateKey(customer => new { customer.Id, customer.ProjectId });
            customerEntity.HasIndex(customer => new { customer.ProjectId, customer.WhatsAppLid });

            modelBuilder.Entity<Modules.Campaigns.Domain.Campaign>()
                .HasOne<Modules.WhatsApp.Domain.WhatsAppAccount>()
                .WithMany()
                .HasForeignKey(campaign => new { campaign.WhatsAppAccountId, campaign.ProjectId })
                .HasPrincipalKey(account => new { account.Id, account.ProjectId })
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Modules.Campaigns.Domain.CampaignRecipient>()
                .HasIndex(recipient => new { recipient.CampaignId, recipient.CustomerId })
                .IsUnique();

            var bookingEntity = modelBuilder.Entity<Modules.GroupAppointments.Domain.GroupAppointmentBooking>();
            bookingEntity.HasIndex(booking => new { booking.ProjectId, booking.CustomerId });

            if (string.Equals(Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            {
                customerEntity
                    .Property<string>(Modules.GroupAppointments.Domain.GroupBookingPhoneFields.CustomerCanonical)
                    .HasComputedColumnSql("public.canonical_group_booking_phone_v1(\"PhoneNumber\")", stored: true);
                customerEntity.HasIndex(
                    nameof(Modules.Conversations.Domain.Customer.ProjectId),
                    Modules.GroupAppointments.Domain.GroupBookingPhoneFields.CustomerCanonical)
                    .HasFilter("\"PhoneNumberCanonical\" IS NOT NULL");

                bookingEntity
                    .Property<string>(Modules.GroupAppointments.Domain.GroupBookingPhoneFields.BookingCanonical)
                    .HasComputedColumnSql("public.canonical_group_booking_phone_v1(\"CustomerPhone\")", stored: true);
                bookingEntity.HasIndex(
                    nameof(Modules.GroupAppointments.Domain.GroupAppointmentBooking.ProjectId),
                    Modules.GroupAppointments.Domain.GroupBookingPhoneFields.BookingCanonical)
                    .HasFilter("\"CustomerPhoneCanonical\" IS NOT NULL");
            }

            modelBuilder.Entity<Modules.GroupAppointments.Domain.GroupAppointment>()
                .HasMany(g => g.Bookings)
                .WithOne(b => b.GroupAppointment)
                .HasForeignKey(b => b.GroupAppointmentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Modules.GroupAppointments.Domain.GroupAppointment>()
                .Property(appointment => appointment.WhatsAppGroupJid)
                .IsConcurrencyToken();
            modelBuilder.Entity<Modules.GroupAppointments.Domain.GroupAppointment>()
                .Property(appointment => appointment.WhatsAppAccountId)
                .IsConcurrencyToken();

            modelBuilder.Entity<Modules.GroupAppointments.Domain.GroupAppointment>()
                .HasOne<Modules.WhatsApp.Domain.WhatsAppAccount>()
                .WithMany()
                .HasForeignKey(appointment => new
                {
                    appointment.WhatsAppAccountId,
                    appointment.ProjectId
                })
                .HasPrincipalKey(account => new { account.Id, account.ProjectId })
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Modules.Facebook.Domain.ConnectedPage>()
                .HasIndex(cp => cp.ProjectId);

            modelBuilder.Entity<Modules.Facebook.Domain.ConnectedPage>()
                .HasIndex(cp => cp.FacebookPageId)
                .IsUnique();

            modelBuilder.Entity<Modules.QuranChallenge.Domain.QuranYouTubeSettings>()
                .HasIndex(settings => settings.ProjectId)
                .IsUnique();

            modelBuilder.Entity<Modules.QuranChallenge.Domain.QuranFacebookSettings>()
                .HasIndex(settings => settings.ProjectId)
                .IsUnique();

            modelBuilder.Entity<Modules.QuranChallenge.Domain.QuranTikTokSettings>()
                .HasIndex(settings => settings.ProjectId)
                .IsUnique();

            modelBuilder.Entity<Modules.Projects.Domain.ProjectSettings>()
                .Property(settings => settings.GeminiEnterpriseProjectId)
                .HasMaxLength(30);

            modelBuilder.Entity<Modules.Projects.Domain.ProjectSettings>()
                .Property(settings => settings.GeminiAgentPlatformApiKey)
                .IsRequired();

            modelBuilder.Entity<Modules.Content.Domain.ContentAutomationSettings>()
                .HasIndex(settings => settings.ProjectId)
                .IsUnique();

            modelBuilder.Entity<Modules.Content.Domain.ContentPost>()
                .HasIndex(post => new { post.ProjectId, post.CreatedAt });

            modelBuilder.Entity<Modules.Content.Domain.ContentPost>()
                .HasIndex(post => new { post.ProjectId, post.ScheduledForUtc });

            modelBuilder.Entity<Modules.Content.Domain.ContentWeekPlan>()
                .HasIndex(plan => new { plan.ProjectId, plan.Status, plan.CreatedAt });

            modelBuilder.Entity<Modules.Content.Domain.ContentWeekPlanItem>()
                .HasIndex(item => new { item.PlanId, item.DayIndex })
                .IsUnique();

            modelBuilder.Entity<Modules.Content.Domain.ContentWeekPlanItem>()
                .HasIndex(item => new { item.ProjectId, item.ScheduledForUtc });

            modelBuilder.Entity<Modules.Content.Domain.ContentWeekPlanItem>()
                .HasOne<Modules.Content.Domain.ContentWeekPlan>()
                .WithMany()
                .HasForeignKey(item => item.PlanId)
                .OnDelete(DeleteBehavior.Cascade);

            var contentVideo = modelBuilder.Entity<Modules.Content.Domain.ContentVideo>();
            contentVideo.HasIndex(video => new { video.ProjectId, video.CreatedAt });
            contentVideo.HasIndex(video => new { video.ProjectId, video.Status, video.UpdatedAt });
            contentVideo.HasIndex(video => new { video.Status, video.UpdatedAt });
            contentVideo.HasAlternateKey(video => new { video.Id, video.ProjectId });
            contentVideo.Property(video => video.Brief).HasMaxLength(2_000);
            contentVideo.Property(video => video.IdeaTitle).HasMaxLength(300);
            contentVideo.Property(video => video.Hook).HasMaxLength(1_000);
            contentVideo.Property(video => video.AspectRatio).HasMaxLength(8);
            contentVideo.Property(video => video.Resolution).HasMaxLength(16);
            contentVideo.Property(video => video.KnowledgeSnapshotHash).HasMaxLength(64);
            contentVideo.Property(video => video.PlannerModel).HasMaxLength(100);
            contentVideo.Property(video => video.VideoModel).HasMaxLength(100);
            contentVideo.Property(video => video.FinalVideoObjectKey).HasMaxLength(1_024);
            contentVideo.Property(video => video.FinalVideoMimeType).HasMaxLength(100);
            contentVideo.Property(video => video.Error).HasMaxLength(1_000);

            var contentVideoScene = modelBuilder.Entity<Modules.Content.Domain.ContentVideoScene>();
            contentVideoScene.HasIndex(scene => new { scene.ContentVideoId, scene.SceneIndex }).IsUnique();
            contentVideoScene.HasIndex(scene => new { scene.ProjectId, scene.Status, scene.UpdatedAt });
            contentVideoScene.HasIndex(scene => new { scene.Status, scene.NextAttemptAtUtc, scene.ProjectId });
            contentVideoScene.Property(scene => scene.Title).HasMaxLength(300);
            contentVideoScene.Property(scene => scene.ProviderInteractionId).HasMaxLength(500);
            contentVideoScene.Property(scene => scene.ProviderProjectId).HasMaxLength(30);
            contentVideoScene.Property(scene => scene.VideoObjectKey).HasMaxLength(1_024);
            contentVideoScene.Property(scene => scene.VideoMimeType).HasMaxLength(100);
            contentVideoScene.Property(scene => scene.Error).HasMaxLength(1_000);
            contentVideoScene
                .HasOne(scene => scene.Video)
                .WithMany(video => video.Scenes)
                .HasForeignKey(scene => new { scene.ContentVideoId, scene.ProjectId })
                .HasPrincipalKey(video => new { video.Id, video.ProjectId })
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingConnection>()
                .HasIndex(x => new { x.ProjectId, x.Provider }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingConnection>()
                .HasIndex(x => new { x.Provider, x.AdAccountExternalId }).IsUnique()
                .HasFilter("\"AdAccountExternalId\" IS NOT NULL AND \"State\" <> 5");
            modelBuilder.Entity<Modules.Advertising.Domain.AuthorizedWhatsAppDestination>()
                .HasIndex(x => new { x.ProjectId, x.ConnectionId, x.PhoneNumberExternalId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AuthorizedWhatsAppDestination>()
                .HasIndex(x => new { x.Provider, x.WabaExternalId, x.PhoneNumberExternalId }).IsUnique()
                .HasFilter("\"State\" <> 5");
            modelBuilder.Entity<Modules.Advertising.Domain.AuthorizedWhatsAppDestination>()
                .HasOne<Modules.WhatsApp.Domain.WhatsAppAccount>()
                .WithMany()
                .HasForeignKey(destination => new
                {
                    destination.WhatsAppAccountId,
                    destination.ProjectId
                })
                .HasPrincipalKey(account => new { account.Id, account.ProjectId })
                .OnDelete(DeleteBehavior.Restrict);
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingCapabilitySnapshot>()
                .HasIndex(x => new { x.ProjectId, x.ConnectionId, x.DestinationId, x.CheckedAtUtc });
            modelBuilder.Entity<Modules.Advertising.Domain.ConnectionDisconnectTarget>()
                .HasIndex(x => new { x.DisconnectOperationId, x.TargetType, x.TargetId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AutonomyEnvelope>()
                .HasIndex(x => new { x.ProjectId, x.State });
            modelBuilder.Entity<Modules.Advertising.Domain.EnvelopeOfferDestinationGrant>()
                .HasIndex(x => new { x.EnvelopeId, x.OfferId, x.DestinationId, x.State }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.EnvelopeAudienceSourceGrant>()
                .HasIndex(x => new { x.EnvelopeId, x.SourceType, x.SourceExternalId, x.State }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingFactSource>()
                .HasIndex(x => new { x.ProjectId, x.ProfileId, x.FactName });
            modelBuilder.Entity<Modules.Advertising.Domain.ConversionSourceEvent>()
                .HasIndex(x => new { x.ProjectId, x.SourceSystem, x.ExternalEventId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingWebhookSource>()
                .HasIndex(x => new { x.ProjectId, x.SourceKey }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.CanonicalConversion>()
                .HasIndex(x => new { x.ProjectId, x.CanonicalKey }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.CanonicalConversion>()
                .HasIndex(x => new { x.ProjectId, x.OccurredAtUtc });
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingCreativeVariant>()
                .HasIndex(x => new { x.ProjectId, x.CreativeId, x.Placement, x.SourceHash }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.CampaignPlan>()
                .HasIndex(x => new { x.ProjectId, x.PlanHash }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AudienceStrategy>()
                .HasIndex(x => new { x.ProjectId, x.EnvelopeId, x.Version });
            modelBuilder.Entity<Modules.Advertising.Domain.CampaignPlanCreative>()
                .HasIndex(x => new { x.PlanId, x.CreativeVariantId, x.Role }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingExperimentArm>()
                .HasIndex(x => new { x.ExperimentId, x.IsControl });
            modelBuilder.Entity<Modules.Advertising.Domain.ManagedOwnershipRecord>()
                .HasIndex(x => new { x.ProjectId, x.ProviderCampaignExternalId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ManagedCampaign>()
                .HasIndex(x => new { x.ProjectId, x.ConnectionId, x.ExternalId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ManagedAdSet>()
                .HasIndex(x => new { x.ProjectId, x.ConnectionId, x.ExternalId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ManagedProviderCreative>()
                .HasIndex(x => new { x.ProjectId, x.ConnectionId, x.ExternalId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ProviderOperation>()
                .HasIndex(x => new { x.ProjectId, x.IdempotencyKey }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ProviderObjectSnapshot>()
                .HasIndex(x => new { x.OperationId, x.ObjectType, x.SnapshotType, x.StateHash }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ConversionAdjustment>()
                .HasIndex(x => new { x.ProjectId, x.ExternalEventId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ExecutionCommand>()
                .HasIndex(x => new { x.ProjectId, x.IdempotencyKey }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.BudgetPeriodLedger>()
                .HasIndex(x => new { x.ProjectId, x.EnvelopeId, x.PeriodKind, x.PeriodStartUtc }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.BudgetAllocationLedgerDebit>()
                .HasIndex(x => new { x.AllocationId, x.LedgerId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.InsightsSnapshot>()
                .HasIndex(x => new { x.ProjectId, x.TargetType, x.TargetId, x.IntervalStartUtc, x.IntervalEndUtc, x.BreakdownHash, x.Revision }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.InsightsSnapshot>()
                .HasIndex(x => new { x.ProjectId, x.TargetType, x.TargetId, x.IntervalStartUtc, x.IntervalEndUtc, x.BreakdownHash, x.IsCurrent });
            modelBuilder.Entity<Modules.Advertising.Domain.InsightsSnapshot>()
                .HasIndex(x => new { x.ProjectId, x.IsCurrent, x.IntervalStartUtc });
            modelBuilder.Entity<Modules.Advertising.Domain.ManagedAdvertisement>()
                .HasIndex(x => new { x.ProjectId, x.AdExternalId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.WhatsAppAttributionObservation>()
                .HasIndex(x => new { x.ProjectId, x.DestinationId, x.MessageExternalId, x.PayloadHash }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.WhatsAppAttributionContext>()
                .HasIndex(x => new { x.ProjectId, x.ConversationId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingAttributionTouch>()
                .HasIndex(x => new { x.ProjectId, x.ObservationId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ConversionDelivery>()
                .HasIndex(x => new { x.ProjectId, x.Provider, x.EventIdentity }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ConversionDeliveryAttempt>()
                .HasIndex(x => new { x.DeliveryId, x.AttemptNumber }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingAiWorkItem>()
                .HasIndex(x => new { x.ProjectId, x.OwnerId, x.OwnerVersion, x.Purpose, x.State });
            modelBuilder.Entity<Modules.Advertising.Domain.TrackingHealthPolicy>()
                .HasIndex(x => new { x.ProjectId, x.Goal, x.Version }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.TrackingHealthSnapshot>()
                .HasIndex(x => new { x.ProjectId, x.DestinationId, x.WindowStartUtc, x.WindowEndUtc });
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingAuditRecord>()
                .HasIndex(x => new { x.ProjectId, x.OccurredAtUtc, x.Id });
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingCycleRun>()
                .HasIndex(x => new { x.ProjectId, x.JobName, x.BucketStartUtc }).IsUnique();
            modelBuilder.Entity<IntegrationOutboxMessage>().HasIndex(x => new { x.PublishedAtUtc, x.NextAttemptAtUtc });
            modelBuilder.Entity<IntegrationOutboxMessage>().HasIndex(x => x.EventId).IsUnique();
            modelBuilder.Entity<IntegrationInboxReceipt>().HasIndex(x => new { x.EventId, x.Consumer }).IsUnique();
            modelBuilder.Entity<IntegrationProjectionWatermark>()
                .HasIndex(x => new { x.ProjectId, x.Consumer, x.SourceAggregateType, x.SourceAggregateId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.ProjectAdvertisingContextProjection>()
                .HasIndex(x => x.ProjectId).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingKnowledgeProjection>()
                .HasIndex(x => new { x.ProjectId, x.DocumentId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingMediaProjection>()
                .HasIndex(x => new { x.ProjectId, x.AssetId }).IsUnique();
            modelBuilder.Entity<Modules.Advertising.Domain.CustomerAdvertisingConsentProjection>()
                .HasIndex(x => new { x.ProjectId, x.CustomerId }).IsUnique();
            modelBuilder.Entity<Modules.WhatsApp.Domain.WhatsAppInboundRouteProjection>()
                .HasIndex(x => new { x.Provider, x.WabaExternalId, x.PhoneNumberExternalId }).IsUnique()
                .HasFilter("\"State\" = 'Active'");
            modelBuilder.Entity<Modules.WhatsApp.Domain.WhatsAppInboundRouteProjection>()
                .HasIndex(x => new { x.ProjectId, x.DestinationId, x.DestinationVersion }).IsUnique();

            modelBuilder.Entity<Modules.Advertising.Domain.AutonomyEnvelope>().Property(x => x.DailyCap).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.AutonomyEnvelope>().Property(x => x.PeriodCap).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.BudgetPeriodLedger>().Property(x => x.AuthorizedCap).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.BudgetAllocation>().Property(x => x.AllocatedAmount).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.BudgetAllocationLedgerDebit>().Property(x => x.ReservedAmount).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.InsightsSnapshot>().Property(x => x.Spend).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.CanonicalConversion>().Property(x => x.CurrentValue).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.CampaignPlan>().Property(x => x.DailyBudget).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingExperiment>().Property(x => x.BudgetCap).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingExperiment>().Property(x => x.MinimumSpend).HasPrecision(18, 4);
            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingExperimentArm>().Property(x => x.AllocatedBudget).HasPrecision(18, 4);

            modelBuilder.Entity<Modules.Advertising.Domain.AdvertisingConnection>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            modelBuilder.Entity<Modules.Advertising.Domain.AuthorizedWhatsAppDestination>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            modelBuilder.Entity<Modules.Advertising.Domain.AutonomyEnvelope>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            modelBuilder.Entity<Modules.Advertising.Domain.ConnectionDisconnectOperation>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            modelBuilder.Entity<Modules.Advertising.Domain.ProviderOperation>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            modelBuilder.Entity<Modules.Advertising.Domain.BudgetPeriodLedger>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            modelBuilder.Entity<Modules.Advertising.Domain.ConversionDelivery>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();
            modelBuilder.Entity<Modules.Advertising.Domain.ExecutionCommand>().Property(x => x.ConcurrencyToken).IsConcurrencyToken();


            // Apply global query filter for all entities implementing ITenantEntity
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                if (typeof(ITenantEntity).IsAssignableFrom(entityType.ClrType))
                {
                    modelBuilder.Entity(entityType.ClrType)
                        .HasQueryFilter(CreateTenantFilterExpression(entityType.ClrType));
                }
            }
        }

        private System.Linq.Expressions.LambdaExpression CreateTenantFilterExpression(Type entityType)
        {
            // e => EF.Property<Guid>(e, "ProjectId") == this.CurrentProjectId
            var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
            var propertyMethod = typeof(EF).GetMethod("Property", new[] { typeof(object), typeof(string) }).MakeGenericMethod(typeof(Guid));
            var propertyCall = System.Linq.Expressions.Expression.Call(null, propertyMethod, parameter, System.Linq.Expressions.Expression.Constant("ProjectId"));
            
            // Reference the DbContext instance (this) and its property "CurrentProjectId"
            var dbContextConstant = System.Linq.Expressions.Expression.Constant(this);
            var tenantProjectId = System.Linq.Expressions.Expression.Property(dbContextConstant, nameof(CurrentProjectId));
            var comparison = System.Linq.Expressions.Expression.Equal(propertyCall, tenantProjectId);
            
            return System.Linq.Expressions.Expression.Lambda(comparison, parameter);
        }

        public override int SaveChanges()
        {
            ApplyTenantAndAuditInfo();
            return base.SaveChanges();
        }

        public IDisposable SuppressEntityIndexEvents()
        {
            _entityIndexEventSuppressionDepth++;
            return new EntityIndexEventSuppression(this);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyTenantAndAuditInfo();

            var deletedProjects = ChangeTracker.Entries<Modules.Projects.Domain.Project>()
                .Where(x => x.State == EntityState.Deleted).Select(x => x.Entity.Id).ToList();
            foreach (var projectId in deletedProjects)
                Shared.Queue.IntegrationOutbox.Enqueue(this, new Shared.Queue.AdvertisingProjectLifecycleChanged
                {
                    ProjectId = projectId, State = "Deleted", SourceAggregateType = "Project", SourceAggregateId = projectId,
                    SourceVersion = 1, IsTombstone = true
                });

            // Intercept and generate audit logs for business entities
            var auditEntries = new System.Collections.Generic.List<Modules.Audit.Domain.AuditLog>();
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.Entity is Modules.Audit.Domain.AuditLog) continue;

                if (entry.State == EntityState.Added || entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                {
                    var entityType = entry.Entity.GetType().Name;
                    if (entityType == "Customer" || entityType == "FollowUp" || entityType == "Deal" || entityType == "Campaign")
                    {
                        var te = entry.Entity as ITenantEntity;
                        var projectId = te?.ProjectId ?? _tenantContext.ProjectId;
                        if (projectId == Guid.Empty)
                        {
                            projectId = _tenantContext.ProjectId;
                        }

                        var auditLog = new Modules.Audit.Domain.AuditLog
                        {
                            ProjectId = projectId,
                            Action = entry.State.ToString() + entityType, // e.g. AddedCustomer, ModifiedCustomer
                            EntityType = entityType,
                            EntityId = entry.Property("Id").CurrentValue?.ToString() ?? Guid.Empty.ToString(),
                            Timestamp = DateTime.UtcNow
                        };

                        if (entry.State == EntityState.Modified)
                        {
                            var originalValues = entry.OriginalValues.Properties.ToDictionary(p => p.Name, p => entry.OriginalValues[p]);
                            var currentValues = entry.CurrentValues.Properties.ToDictionary(p => p.Name, p => entry.CurrentValues[p]);
                            auditLog.OriginalState = JsonSerializer.Serialize(originalValues);
                            auditLog.NewState = JsonSerializer.Serialize(currentValues);
                        }
                        else if (entry.State == EntityState.Added)
                        {
                            var currentValues = entry.CurrentValues.Properties.ToDictionary(p => p.Name, p => entry.CurrentValues[p]);
                            auditLog.NewState = JsonSerializer.Serialize(currentValues);
                        }
                        else if (entry.State == EntityState.Deleted)
                        {
                            var originalValues = entry.OriginalValues.Properties.ToDictionary(p => p.Name, p => entry.OriginalValues[p]);
                            auditLog.OriginalState = JsonSerializer.Serialize(originalValues);
                        }

                        auditEntries.Add(auditLog);
                    }
                }
            }

            if (auditEntries.Any())
            {
                AuditLogs.AddRange(auditEntries);
            }

            var changedEntities = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified || e.State == EntityState.Deleted)
                .Select(e => new
                {
                    State = e.State,
                    Entity = e.Entity
                })
                .ToList();

            var result = await base.SaveChangesAsync(cancellationToken);

            if (_entityIndexEventSuppressionDepth == 0)
            {
                try
                {
                    var eventBus = (Shared.Queue.IEventBus?)_serviceProvider.GetService(typeof(Shared.Queue.IEventBus));
                    if (eventBus != null)
                    {
                        foreach (var change in changedEntities)
                        {
                            string? entityType = null;
                            Guid entityId = Guid.Empty;
                            Guid projectId = Guid.Empty;

                            if (change.Entity is Modules.Conversations.Domain.Message msg)
                            {
                                entityType = "Message";
                                entityId = msg.Id;
                                var convo = Conversations.Local.FirstOrDefault(c => c.Id == msg.ConversationId)
                                            ?? await Conversations.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == msg.ConversationId, cancellationToken);
                                projectId = convo?.ProjectId ?? _tenantContext.ProjectId;
                            }
                            else if (change.Entity is Modules.Conversations.Domain.Customer cust)
                            {
                                entityType = "Customer";
                                entityId = cust.Id;
                                projectId = cust.ProjectId;
                            }
                            else if (change.Entity is Modules.Conversations.Domain.Conversation convo)
                            {
                                entityType = "Conversation";
                                entityId = convo.Id;
                                projectId = convo.ProjectId;
                            }

                            if (entityType != null)
                            {
                                await eventBus.PublishAsync(new Shared.Events.EntityIndexedEvent
                                {
                                    EntityId = entityId,
                                    EntityType = entityType,
                                    ProjectId = projectId,
                                    Action = change.State == EntityState.Deleted ? "Delete" : "Upsert",
                                    ContentJson = JsonSerializer.Serialize(change.Entity)
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to publish index events: {ex.Message}");
                }
            }

            return result;
        }

        private void EndEntityIndexEventSuppression()
        {
            if (_entityIndexEventSuppressionDepth > 0)
            {
                _entityIndexEventSuppressionDepth--;
            }
        }

        private sealed class EntityIndexEventSuppression(AppDbContext context) : IDisposable
        {
            private AppDbContext? _context = context;

            public void Dispose()
            {
                _context?.EndEntityIndexEventSuppression();
                _context = null;
            }
        }

        private void ApplyTenantAndAuditInfo()
        {
            var entries = ChangeTracker.Entries();
            foreach (var entry in entries)
            {
                if (entry.Entity is ITenantEntity tenantEntity)
                {
                    if (entry.State == EntityState.Added)
                    {
                        if (tenantEntity.ProjectId == Guid.Empty)
                        {
                            tenantEntity.ProjectId = _tenantContext.ProjectId;
                        }
                    }
                }

                if (entry.Entity is AuditableEntity auditableEntity)
                {
                    if (entry.State == EntityState.Added)
                    {
                        auditableEntity.CreatedAt = DateTime.UtcNow;
                        auditableEntity.UpdatedAt = DateTime.UtcNow;
                    }
                    else if (entry.State == EntityState.Modified)
                    {
                        auditableEntity.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }
        }
    }
}
