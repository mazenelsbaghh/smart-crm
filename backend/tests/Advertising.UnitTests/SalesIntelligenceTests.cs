using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.AI.Services;
using Modules.Analytics.Application;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class SalesIntelligenceTests
{
    [Fact]
    public void Parser_caps_ai_stage_before_verified_booking_truth_and_rejects_converted_claim()
    {
        var parsed = SalesIntelligenceAiParser.ParseConversation("""
            {"stage":"Paid","outcome":"Converted","primaryReason":"PriceObjection","secondaryReasons":[],
            "summary":"ملخص","recommendation":"تابع","evidence":[],"lastCustomerIntent":"حجز",
            "confidence":1.7,"replyQualityScore":140,"followUpPriority":-4,"needsFollowUp":true,"missedOpportunity":true}
            """);

        Assert.Equal(SalesConversationStage.BookingIntent, parsed.Stage);
        Assert.Equal(SalesConversationOutcome.Active, parsed.Outcome);
        Assert.Equal(1m, parsed.Confidence);
        Assert.Equal(100, parsed.ReplyQualityScore);
        Assert.Equal(0, parsed.FollowUpPriority);
    }

    [Fact]
    public void Parser_rejects_incomplete_ai_analysis_instead_of_saving_false_defaults()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SalesIntelligenceAiParser.ParseConversation("{}"));
    }

    [Fact]
    public async Task Paid_booking_truth_overrides_ai_loss_reason_and_follow_up()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        db.GroupAppointmentBookings.Add(new GroupAppointmentBooking
        {
            ProjectId = setup.ProjectId,
            CustomerId = conversation.CustomerId,
            GroupAppointmentId = Guid.NewGuid(),
            CustomerName = "عميل",
            CustomerPhone = "01000000000",
            IsPaid = true,
            CreatedAt = conversation.CreatedAt.AddHours(2)
        });
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());

        var analysis = await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);

        Assert.Equal(SalesConversationStage.Paid, analysis.VerifiedStage);
        Assert.Equal(SalesConversationOutcome.Converted, analysis.Outcome);
        Assert.Equal(SalesLossReason.None, analysis.AiPrimaryReason);
        Assert.False(analysis.NeedsFollowUp);
        Assert.Equal(0, analysis.FollowUpPriority);
    }

    [Fact]
    public async Task Manual_reason_is_preserved_as_effective_reason_after_correction()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);

        var userId = Guid.NewGuid();
        await analyzer.CorrectAsync(
            new(setup.ProjectId, conversation.Id, SalesLossReason.ScheduleMismatch,
                "العميل أكد أن الموعد غير مناسب", userId),
            CancellationToken.None);
        var corrected = await analyzer.GetAsync(setup.ProjectId, conversation.Id, CancellationToken.None);

        Assert.NotNull(corrected);
        Assert.Equal(SalesLossReason.ScheduleMismatch, corrected!.EffectivePrimaryReason);
        Assert.Equal(userId, corrected.CorrectedByUserId);
        Assert.NotNull(corrected.CorrectedAtUtc);
    }

    [Fact]
    public async Task Analysis_requires_the_key_configured_for_the_same_project()
    {
        var setup = CreateDatabase(withGeminiKey: false);
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None));

        Assert.Contains("إعدادات هذا المشروع", error.Message);
    }

    [Fact]
    public async Task Dashboard_2026_08_30_regression_excludes_active_chats_from_loss_reasons()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        var analysis = await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        analysis.Outcome = SalesConversationOutcome.Active;
        analysis.AiPrimaryReason = SalesLossReason.None;
        await db.SaveChangesAsync();
        var service = new SalesIntelligenceService(db, new FakeGemini(), new PassthroughVault());

        var dashboard = await service.GetDashboardAsync(
            setup.ProjectId,
            conversation.CreatedAt.AddMinutes(-1),
            conversation.CreatedAt.AddDays(1),
            CancellationToken.None);

        Assert.Empty(dashboard.Reasons);
        Assert.Single(dashboard.Opportunities);
        var intentDropOff = dashboard.FunnelTransitions.Single(item => item.Key == "intent-to-booked");
        Assert.Equal(1, intentDropOff.DropOffCount);
        Assert.Equal("StillActive", Assert.Single(intentDropOff.Reasons).Reason);
        Assert.Equal(1, intentDropOff.NeedsFollowUp);
    }

    [Fact]
    public async Task Dashboard_does_not_publish_action_links_for_unsupported_conversation_channels()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        conversation.Channel = "Email";
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        var service = new SalesIntelligenceService(db, new FakeGemini(), new PassthroughVault());

        var dashboard = await service.GetDashboardAsync(
            setup.ProjectId,
            conversation.CreatedAt.AddMinutes(-1),
            conversation.CreatedAt.AddDays(1),
            CancellationToken.None);

        Assert.Empty(dashboard.Opportunities);
        Assert.Equal(0, dashboard.FollowUpPlan.SendNow);
        Assert.Equal("Email", Assert.Single(dashboard.Analyses).Channel);
    }

    [Fact]
    public async Task Messenger_opportunity_requires_manual_reply_and_is_excluded_from_automated_plans()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        conversation.Channel = "Messenger";
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        var service = new SalesIntelligenceService(db, new FakeGemini(), new PassthroughVault());
        var fromUtc = conversation.CreatedAt.AddMinutes(-1);
        var toUtc = conversation.CreatedAt.AddDays(1);

        var dashboard = await service.GetDashboardAsync(
            setup.ProjectId,
            fromUtc,
            toUtc,
            CancellationToken.None);
        var queued = await service.QueueFollowUpPlanAsync(
            new(setup.ProjectId, fromUtc, toUtc, FollowUpPlanAction.SendNow),
            CancellationToken.None);

        Assert.Equal("OpenConversation", Assert.Single(dashboard.Opportunities).RecommendedAction);
        Assert.Equal(0, dashboard.FollowUpPlan.SendNow);
        Assert.Equal(0, dashboard.FollowUpPlan.Schedule);
        Assert.Equal(0, queued.Queued);
        Assert.Empty(db.FollowUps);
    }

    [Fact]
    public async Task Dashboard_uses_entry_cohort_verified_booking_and_real_message_timestamps()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var bookedConversation = SeedConversation(db, setup.ProjectId);
        db.Messages.Add(new Message
        {
            ConversationId = bookedConversation.Id,
            ExternalMessageId = Guid.NewGuid().ToString("N"),
            Direction = "Outgoing",
            Content = "أهلًا بك",
            MessageType = "Text",
            Timestamp = bookedConversation.LastMessageTimestamp.AddMinutes(4)
        });
        var secondConversation = SeedConversation(db, setup.ProjectId);
        secondConversation.CreatedAt = bookedConversation.CreatedAt.AddHours(1);
        secondConversation.LastMessageTimestamp = secondConversation.CreatedAt.AddMinutes(2);
        db.GroupAppointmentBookings.Add(new GroupAppointmentBooking
        {
            ProjectId = setup.ProjectId,
            CustomerId = bookedConversation.CustomerId,
            GroupAppointmentId = Guid.NewGuid(),
            CustomerName = "عميل",
            CustomerPhone = "01000000000",
            IsPaid = true,
            CreatedAt = bookedConversation.CreatedAt.AddHours(2)
        });
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, bookedConversation.Id, CancellationToken.None);
        await analyzer.ReanalyzeAsync(setup.ProjectId, secondConversation.Id, CancellationToken.None);
        db.FollowUps.Add(new FollowUp
        {
            ProjectId = setup.ProjectId,
            CustomerId = secondConversation.CustomerId,
            DueDate = DateTime.UtcNow.AddHours(24),
            Status = "Pending",
            Notes = "اعرض بدائل مناسبة."
        });
        await db.SaveChangesAsync();
        var service = new SalesIntelligenceService(db, new FakeGemini(), new PassthroughVault());

        var dashboard = await service.GetDashboardAsync(
            setup.ProjectId,
            bookedConversation.CreatedAt.AddMinutes(-1),
            secondConversation.CreatedAt.AddDays(1),
            CancellationToken.None);

        Assert.Equal(2, dashboard.TotalConversations);
        Assert.Equal(50m, dashboard.BookingConversionRate);
        Assert.Equal(50m, dashboard.PaymentConversionRate);
        Assert.Equal(4m, dashboard.MedianFirstResponseMinutes);
        Assert.Equal(1, dashboard.Funnel.Single(item => item.Key == "booked").Count);
        Assert.Equal(6, dashboard.FunnelTransitions.Count);
        var responseTransition = dashboard.FunnelTransitions.Single(item => item.Key == "new-to-responded");
        Assert.Equal(2, responseTransition.FromCount);
        Assert.Equal(1, responseTransition.ToCount);
        var intentTransition = dashboard.FunnelTransitions.Single(item => item.Key == "intent-to-booked");
        Assert.Equal(2, intentTransition.FromCount);
        Assert.Equal(1, intentTransition.ToCount);
        Assert.Equal(50m, intentTransition.ConversionRate);
        var attendanceTransition = dashboard.FunnelTransitions.Single(item => item.Key == "paid-to-attended");
        Assert.Equal(1, attendanceTransition.DropOffCount);
        Assert.Equal("AttendanceNotRecorded", Assert.Single(attendanceTransition.Reasons).Reason);
        Assert.Equal(0, dashboard.FollowUpPlan.SendNow);
        Assert.Equal(1, dashboard.FollowUpPlan.Scheduled);
        Assert.Equal("Scheduled", Assert.Single(dashboard.Opportunities).RecommendedAction);
    }

    [Fact]
    public async Task Analyst_answer_reports_the_exact_analysis_scope()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        var answerGemini = new FakeGemini($$"""
            {"answer":"الاعتراض على السعر هو السبب الظاهر.","conversationIds":["{{conversation.Id}}"]}
            """);
        var service = new SalesIntelligenceService(db, answerGemini, new PassthroughVault());

        var answer = await service.AskAsync(
            new(setup.ProjectId, conversation.CreatedAt.AddMinutes(-1), conversation.CreatedAt.AddDays(1), "ليه الحجوزات قلت؟"),
            CancellationToken.None);

        Assert.Equal(1, answer.TotalConversations);
        Assert.Equal(1, answer.AnalyzedConversations);
        Assert.Equal(1, answer.DetailedAnalysesReviewed);
        Assert.Equal(100m, answer.AnalysisCoverage);
        Assert.Equal(conversation.Id, Assert.Single(answer.ConversationIds));
    }

    [Fact]
    public async Task Analyst_reviews_every_analysis_in_batches_before_synthesis()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var startedAt = DateTime.UtcNow.AddHours(-8);
        var oldestConversationId = Guid.Empty;

        for (var index = 0; index < 76; index++)
        {
            var customerId = Guid.NewGuid();
            var conversationId = Guid.NewGuid();
            if (index == 0) oldestConversationId = conversationId;
            var conversationStartedAt = startedAt.AddMinutes(index);
            db.Customers.Add(new Customer
            {
                Id = customerId,
                ProjectId = setup.ProjectId,
                Name = $"عميل {index}",
                PhoneNumber = $"010{index:D8}",
                City = "القاهرة"
            });
            db.Conversations.Add(new Conversation
            {
                Id = conversationId,
                ProjectId = setup.ProjectId,
                CustomerId = customerId,
                Channel = "WhatsApp",
                Status = "Open",
                CreatedAt = conversationStartedAt,
                LastMessageTimestamp = conversationStartedAt.AddMinutes(5)
            });
            db.ConversationSalesAnalyses.Add(new ConversationSalesAnalysis
            {
                ProjectId = setup.ProjectId,
                ConversationId = conversationId,
                CustomerId = customerId,
                ConversationStartedAtUtc = conversationStartedAt,
                LastMessageAtUtc = conversationStartedAt.AddMinutes(5),
                AnalyzedThroughMessageAtUtc = conversationStartedAt.AddMinutes(5),
                AnalyzedAtUtc = conversationStartedAt.AddMinutes(6),
                AiStage = SalesConversationStage.BookingIntent,
                VerifiedStage = SalesConversationStage.BookingIntent,
                Outcome = SalesConversationOutcome.Lost,
                AiPrimaryReason = SalesLossReason.PriceObjection,
                Summary = "العميل اعترض على السعر.",
                Recommendation = "وضّح القيمة.",
                LastCustomerIntent = "معرفة السعر",
                Confidence = 0.9m,
                NeedsFollowUp = true,
                Model = "gemini-3.5-flash"
            });
        }

        await db.SaveChangesAsync();
        var gemini = new AllEvidenceGemini(oldestConversationId);
        var analyzer = new ConversationSalesAnalyzer(db, gemini, new PassthroughVault());
        var service = new SalesIntelligenceService(db, gemini, new PassthroughVault());

        var answer = await service.AskAsync(
            new(setup.ProjectId, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow.AddHours(1), "ليه نية الحجز أعلى من الحجز؟"),
            CancellationToken.None);

        Assert.Equal(76, answer.TotalConversations);
        Assert.Equal(76, answer.AnalyzedConversations);
        Assert.Equal(76, answer.DetailedAnalysesReviewed);
        Assert.Contains("الدليل الأقدم", answer.Answer);
    }

    [Fact]
    public async Task Explicit_alternative_schedule_is_extracted_and_listed_in_demand_sheet()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        db.Messages.Local.Single().Content = "المواعيد دي مش مناسبة، أنا ينفع الجمعة بعد الساعة 6";
        await db.SaveChangesAsync();
        var gemini = new FakeGemini("""
            {"stage":"BookingIntent","outcome":"Lost","primaryReason":"ScheduleMismatch","secondaryReasons":[],
            "summary":"العميل طلب موعدًا بديلًا.","recommendation":"تواصل عند فتح الموعد.","evidence":[],
            "lastCustomerIntent":"طلب موعد بديل","requestedScheduleText":"الجمعة بعد الساعة 6",
            "requestedScheduleLabel":"الجمعة مساءً","confidence":0.95,"replyQualityScore":70,
            "followUpPriority":90,"needsFollowUp":true,"missedOpportunity":true}
            """);
        var analyzer = new ConversationSalesAnalyzer(db, gemini, new PassthroughVault());

        var analysis = await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        var service = new SalesIntelligenceService(db, gemini, new PassthroughVault());
        var sheet = await service.GetScheduleDemandAsync(
            setup.ProjectId, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), CancellationToken.None);

        Assert.Equal("الجمعة بعد الساعة 6", analysis.RequestedScheduleText);
        Assert.Equal("الجمعة مساءً", analysis.RequestedScheduleLabel);
        Assert.Equal(1, sheet.TotalPeople);
        Assert.Equal("الجمعة مساءً", Assert.Single(sheet.Groups).Label);
        Assert.Equal(conversation.Id, Assert.Single(sheet.Rows).ConversationId);
    }

    [Fact]
    public async Task Invented_schedule_is_rejected_when_customer_did_not_write_it()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        db.Messages.Local.Single().Content = "المواعيد دي مش مناسبة";
        await db.SaveChangesAsync();
        var gemini = new FakeGemini("""
            {"stage":"BookingIntent","outcome":"Lost","primaryReason":"ScheduleMismatch","secondaryReasons":[],
            "summary":"المواعيد غير مناسبة.","recommendation":"اسأل عن الموعد المناسب.","evidence":[],
            "lastCustomerIntent":"رفض المواعيد","requestedScheduleText":"الجمعة بعد الساعة 6",
            "requestedScheduleLabel":"الجمعة مساءً","confidence":0.8,"replyQualityScore":60,
            "followUpPriority":85,"needsFollowUp":true,"missedOpportunity":true}
            """);
        var analyzer = new ConversationSalesAnalyzer(db, gemini, new PassthroughVault());

        var analysis = await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);

        Assert.Empty(analysis.RequestedScheduleText);
        Assert.Empty(analysis.RequestedScheduleLabel);
    }

    [Fact]
    public async Task Bulk_send_plan_queues_each_customer_once_for_immediate_delivery()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        var service = new SalesIntelligenceService(db, new FakeGemini(), new PassthroughVault());
        var fromUtc = conversation.CreatedAt.AddMinutes(-1);
        var toUtc = DateTime.UtcNow.AddMinutes(1);
        var dashboard = await service.GetDashboardAsync(
            setup.ProjectId, fromUtc, toUtc, CancellationToken.None);
        var window = new QueueFollowUpPlan(
            setup.ProjectId,
            fromUtc,
            toUtc,
            FollowUpPlanAction.SendNow,
            PlanToken: dashboard.FollowUpPlan.SendNowToken);

        var first = await service.QueueFollowUpPlanAsync(window, CancellationToken.None);
        var second = await service.QueueFollowUpPlanAsync(window, CancellationToken.None);

        Assert.Equal(1, first.Queued);
        Assert.Equal(0, second.Queued);
        Assert.True(second.PlanChanged);
        var followUp = await db.FollowUps.SingleAsync();
        Assert.True(followUp.DueDate <= DateTime.UtcNow);
        Assert.Equal("Pending", followUp.Status);
    }

    [Fact]
    public async Task Individual_send_command_is_snapshot_bound_and_does_not_duplicate_on_retry()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        var service = new SalesIntelligenceService(db, new FakeGemini(), new PassthroughVault());
        var fromUtc = conversation.CreatedAt.AddMinutes(-1);
        var toUtc = DateTime.UtcNow.AddMinutes(1);
        var dashboard = await service.GetDashboardAsync(setup.ProjectId, fromUtc, toUtc, CancellationToken.None);
        var opportunity = Assert.Single(dashboard.Opportunities);
        var command = new QueueFollowUpPlan(
            setup.ProjectId,
            fromUtc,
            toUtc,
            FollowUpPlanAction.SendNow,
            conversation.Id,
            opportunity.ActionToken);

        var first = await service.QueueFollowUpPlanAsync(command, CancellationToken.None);
        var retry = await service.QueueFollowUpPlanAsync(command, CancellationToken.None);

        Assert.Equal(1, first.Queued);
        Assert.Equal(0, retry.Queued);
        Assert.True(retry.PlanChanged);
        var followUp = Assert.Single(db.FollowUps);
        Assert.Equal(conversation.Id, followUp.ConversationId);
        Assert.Equal("WhatsApp", followUp.Channel);
    }

    [Fact]
    public async Task Opportunity_ignores_unrelated_reminder_but_stays_suppressed_while_targeted_send_is_processing()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        var newerConversation = new Conversation
        {
            ProjectId = setup.ProjectId,
            CustomerId = conversation.CustomerId,
            Channel = "WhatsApp",
            Status = "Open",
            CreatedAt = conversation.CreatedAt.AddHours(1),
            LastMessageTimestamp = conversation.LastMessageTimestamp.AddHours(1)
        };
        db.Conversations.Add(newerConversation);
        db.Messages.Add(new Message
        {
            ConversationId = newerConversation.Id,
            ExternalMessageId = Guid.NewGuid().ToString("N"),
            Direction = "Incoming",
            Content = "لسه محتاج أعرف التفاصيل",
            MessageType = "Text",
            Timestamp = newerConversation.LastMessageTimestamp
        });
        db.FollowUps.Add(new FollowUp
        {
            ProjectId = setup.ProjectId,
            CustomerId = conversation.CustomerId,
            DueDate = DateTime.UtcNow.AddHours(1),
            Status = "Pending",
            Type = "AppointmentReminder",
            Notes = "تذكير منفصل"
        });
        await db.SaveChangesAsync();
        var analyzer = new ConversationSalesAnalyzer(db, new FakeGemini(), new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        await analyzer.ReanalyzeAsync(setup.ProjectId, newerConversation.Id, CancellationToken.None);
        var service = new SalesIntelligenceService(db, new FakeGemini(), new PassthroughVault());
        var fromUtc = conversation.CreatedAt.AddMinutes(-1);
        var toUtc = DateTime.UtcNow.AddMinutes(1);

        var beforeClaim = await service.GetDashboardAsync(
            setup.ProjectId, fromUtc, toUtc, CancellationToken.None);
        Assert.Equal("SendNow", Assert.Single(beforeClaim.Opportunities).RecommendedAction);

        db.FollowUps.Add(new FollowUp
        {
            ProjectId = setup.ProjectId,
            CustomerId = conversation.CustomerId,
            ConversationId = conversation.Id,
            Channel = "WhatsApp",
            DueDate = DateTime.UtcNow,
            Status = "Processing",
            Type = "Nurturing",
            Notes = "متابعة المبيعات"
        });
        await db.SaveChangesAsync();

        var duringDispatch = await service.GetDashboardAsync(
            setup.ProjectId, fromUtc, toUtc, CancellationToken.None);
        Assert.Equal(0, duringDispatch.FollowUpPlan.SendNow);
        Assert.Equal(1, duringDispatch.FollowUpPlan.Scheduled);
        var scheduledOpportunity = Assert.Single(duringDispatch.Opportunities);
        Assert.Equal("Scheduled", scheduledOpportunity.RecommendedAction);
        Assert.Equal(conversation.Id, scheduledOpportunity.ConversationId);
    }

    [Fact]
    public async Task Schedule_availability_send_uses_open_city_eligible_groups_and_deduplicates()
    {
        var setup = CreateDatabase();
        await using var db = setup.Db;
        var conversation = SeedConversation(db, setup.ProjectId);
        db.Messages.Local.Single().Content = "المواعيد مش مناسبة، أنا ينفع الجمعة بعد الساعة 6";
        var online = new GroupAppointment
        {
            ProjectId = setup.ProjectId,
            Name = "مجموعة الجمعة أونلاين",
            Mode = "online",
            DateTime = DateTime.UtcNow.AddDays(2),
            Capacity = 5,
            IsActive = true
        };
        var offline = new GroupAppointment
        {
            ProjectId = setup.ProjectId,
            Name = "مجموعة السنتر",
            Mode = "offline",
            DateTime = DateTime.UtcNow.AddDays(3),
            Capacity = 5,
            IsActive = true
        };
        var full = new GroupAppointment
        {
            ProjectId = setup.ProjectId,
            Name = "مجموعة ممتلئة",
            Mode = "online",
            DateTime = DateTime.UtcNow.AddDays(4),
            Capacity = 1,
            IsActive = true
        };
        db.GroupAppointments.AddRange(online, offline, full);
        db.GroupAppointmentBookings.Add(new GroupAppointmentBooking
        {
            ProjectId = setup.ProjectId,
            GroupAppointmentId = full.Id,
            CustomerId = Guid.NewGuid(),
            CustomerName = "محجوز",
            CustomerPhone = "01111111111"
        });
        await db.SaveChangesAsync();

        var gemini = new FakeGemini("""
            {"stage":"BookingIntent","outcome":"Lost","primaryReason":"ScheduleMismatch","secondaryReasons":[],
            "summary":"طلب موعد بديل.","recommendation":"أرسل المتاح.","evidence":[],
            "lastCustomerIntent":"طلب موعد","requestedScheduleText":"الجمعة بعد الساعة 6",
            "requestedScheduleLabel":"الجمعة مساءً","confidence":0.95,"replyQualityScore":70,
            "followUpPriority":90,"needsFollowUp":true,"missedOpportunity":true}
            """);
        var analyzer = new ConversationSalesAnalyzer(db, gemini, new PassthroughVault());
        await analyzer.ReanalyzeAsync(setup.ProjectId, conversation.Id, CancellationToken.None);
        var service = new SalesIntelligenceService(db, gemini, new PassthroughVault());

        var first = await service.QueueScheduleAvailabilityAsync(
            setup.ProjectId, [conversation.CustomerId], CancellationToken.None);
        var second = await service.QueueScheduleAvailabilityAsync(
            setup.ProjectId, [conversation.CustomerId], CancellationToken.None);

        Assert.Equal(1, first.Queued);
        Assert.Equal(0, second.Queued);
        Assert.Equal(1, second.SkippedDuplicate);
        var followUp = await db.FollowUps.SingleAsync();
        Assert.Contains(online.Name, followUp.Notes);
        Assert.DoesNotContain(offline.Name, followUp.Notes);
        Assert.DoesNotContain(full.Name, followUp.Notes);
        Assert.Equal("ScheduleAvailability", followUp.Type);
        Assert.True(followUp.DueDate < DateTime.UtcNow);
    }

    private static Conversation SeedConversation(AppDbContext db, Guid projectId)
    {
        var customer = new Customer
        {
            ProjectId = projectId,
            Name = "عميل",
            PhoneNumber = "01000000000",
            City = "القاهرة"
        };
        var startedAt = DateTime.UtcNow.AddHours(-6);
        var conversation = new Conversation
        {
            ProjectId = projectId,
            CustomerId = customer.Id,
            Channel = "WhatsApp",
            Status = "Open",
            CreatedAt = startedAt,
            LastMessageTimestamp = startedAt.AddMinutes(5)
        };
        db.Customers.Add(customer);
        db.Conversations.Add(conversation);
        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            ExternalMessageId = Guid.NewGuid().ToString("N"),
            Direction = "Incoming",
            Content = "السعر غالي شوية",
            MessageType = "Text",
            Timestamp = conversation.LastMessageTimestamp
        });
        return conversation;
    }

    private static Setup CreateDatabase(bool withGeminiKey = true)
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
        if (withGeminiKey) db.ProjectSettings.Add(new Modules.Projects.Domain.ProjectSettings
        {
            ProjectId = projectId,
            GeminiApiKey = "test-project-key",
            GeminiModel = "gemini-3.5-flash"
        });
        return new Setup(projectId, db);
    }

    private sealed record Setup(Guid ProjectId, AppDbContext Db);

    private sealed class PassthroughVault : IProjectSecretVault
    {
        public bool IsProtected(string? storedValue) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? storedValue) => storedValue;
    }

    private sealed class FakeGemini(string? response = null) : IGeminiClient
    {
        public Task<string> GenerateReplyAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null)
        {
            return Task.FromResult(response ?? """
                {"stage":"BookingIntent","outcome":"Lost","primaryReason":"PriceObjection","secondaryReasons":[],
                "summary":"العميل اعترض على السعر.","recommendation":"وضّح القيمة.","evidence":[],
                "lastCustomerIntent":"معرفة السعر","confidence":0.92,"replyQualityScore":66,
                "followUpPriority":88,"needsFollowUp":true,"missedOpportunity":true}
                """);
        }
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string? apiKeyOverride = null) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string? apiKeyOverride = null) => throw new NotSupportedException();
    }

    private sealed class AllEvidenceGemini(Guid oldestConversationId) : IGeminiClient
    {
        public Task<string> GenerateReplyAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null)
        {
            var includesOldestEvidence = messageContent.Contains(oldestConversationId.ToString(), StringComparison.OrdinalIgnoreCase)
                || messageContent.Contains("الدليل الأقدم", StringComparison.Ordinal);
            var answer = includesOldestEvidence ? "الإجابة تشمل الدليل الأقدم." : "نتيجة دفعة أخرى.";
            var conversationIds = includesOldestEvidence ? $"[\"{oldestConversationId}\"]" : "[]";
            return Task.FromResult($$"""{"answer":"{{answer}}","conversationIds":{{conversationIds}}}""");
        }

        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string? apiKeyOverride = null, string? modelOverride = null, string? cachedContentId = null) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string? apiKeyOverride = null) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string? apiKeyOverride = null, string? modelOverride = null) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string? apiKeyOverride = null) => throw new NotSupportedException();
    }
}
