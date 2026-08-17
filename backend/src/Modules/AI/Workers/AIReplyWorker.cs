using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Modules.AI.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Modules.Conversations.Domain;
using Modules.Conversations.Services;
using Modules.GroupAppointments.Domain;
using Modules.CRM.Domain;

namespace Modules.AI.Workers
{
    public class AIReplyWorker : IIntegrationEventHandler<MessageAggregatedEvent>
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IAIMarketingBrain _aiMarketingBrain;
        private readonly IEventBus _eventBus;
        private readonly ILogger<AIReplyWorker> _logger;

        public AIReplyWorker(
            IServiceProvider serviceProvider,
            IAIMarketingBrain aiMarketingBrain,
            IEventBus eventBus,
            ILogger<AIReplyWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _aiMarketingBrain = aiMarketingBrain;
            _eventBus = eventBus;
            _logger = logger;
        }

        private async Task ApplyKnowledgePricingGuardAsync(AppDbContext dbContext, Guid projectId, string customerMessage, MarketingAnalysisResult analysisResult)
        {
            if (!PricingGuard.IsPricingQuestion(customerMessage))
            {
                return;
            }

            var knowledgeText = await dbContext.KnowledgeDocuments
                .IgnoreQueryFilters()
                .Where(d => d.ProjectId == projectId)
                .Select(d => d.Content)
                .ToListAsync();

            var pricingReply = PricingGuard.BuildPricingReplyFromKnowledge(string.Join("\n\n", knowledgeText));
            if (string.IsNullOrWhiteSpace(pricingReply))
            {
                return;
            }

            analysisResult.Intent = "inquiry";
            analysisResult.Label = "استفسار عن السعر";
            analysisResult.ReplyStyle = "Sales";
            analysisResult.ReplyContent = pricingReply;
            analysisResult.Confidence = Math.Max(analysisResult.Confidence, 0.99);
            analysisResult.SuggestedReaction ??= "😮";
            _logger.LogInformation("Applied knowledge pricing guard to prevent hallucinated pricing.");
        }

        public async Task HandleAsync(MessageAggregatedEvent @event)
        {
            Console.WriteLine($"[AIReplyWorker] Received aggregated message for Project: {@event.ProjectId}, Sender: {@event.Sender}");

            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetProjectId(@event.ProjectId);

            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var configuration = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            var channel = @event.Channel ?? "WhatsApp";
            var aiBehaviorSettingsService = scope.ServiceProvider.GetRequiredService<Modules.AI.Services.IAIBehaviorSettingsService>();

            try
            {

                // Find customer — lookup by PhoneNumber for WhatsApp, by FacebookPSID for Facebook channels
                Customer customer;
                if (channel == "WhatsApp")
                {
                    customer = await dbContext.Customers
                        .FirstOrDefaultAsync(c => c.PhoneNumber == @event.Sender);
                }
                else
                {
                    customer = await dbContext.Customers
                        .FirstOrDefaultAsync(c => c.FacebookPSID == @event.Sender);
                }

            // Query ProjectSettings
            var settings = await dbContext.ProjectSettings
                .FirstOrDefaultAsync(s => s.ProjectId == @event.ProjectId);

            if (settings == null)
            {
                Console.WriteLine($"[AIReplyWorker] ProjectSettings not found for project {@event.ProjectId}. Skipping AI reply.");
                return;
            }

            var systemPromptForReply = settings.SystemPrompt;
            if (settings.HumanTransferEnabled && !string.IsNullOrEmpty(settings.HumanTransferPhone))
            {
                systemPromptForReply = (systemPromptForReply ?? "") + "\n\nCRITICAL CONTACT/PAYMENT RULE:\n" +
                    "- Provide the human/payment contact phone when the customer explicitly asks to talk/connect to a human agent, supervisor, manager, owner, real person, asks for a phone number, asks someone to call them, or clearly wants to pay / asks for payment methods / transfer details / Vodafone Cash / how to pay (e.g. 'عايز أكلم خدمة العملاء', 'شخص حقيقي', 'كلمني حد', 'ممكن رقم الإدارة', 'عايز أدفع', 'أدفع إزاي', 'طرق الدفع', 'رقم فودافون كاش'). In that case, direct them to this phone number: " + settings.HumanTransferPhone + ". If the customer only asks about price/cost (e.g. 'السعر كام', 'بكام', 'التكلفة كام') without saying they want to pay or asking for payment method, answer with the exact price from the knowledge base and do NOT include this phone number.";
            }

            var aiBehaviorSettings = aiBehaviorSettingsService.Resolve(settings, channel);

            // Check per-channel AI auto-reply toggle
            bool isAiEnabled;
            switch (channel)
            {
                case "Messenger":
                    isAiEnabled = settings.MessengerAiAutoReplyEnabled;
                    break;
                case "FacebookComment":
                    isAiEnabled = settings.CommentsAiAutoReplyEnabled;
                    break;
                default: // WhatsApp
                    isAiEnabled = settings.AiAutoReplyEnabled;
                    break;
            }

            if (!isAiEnabled)
            {
                Console.WriteLine($"[AIReplyWorker] AI Auto-Reply is disabled for channel {channel} in project {@event.ProjectId}. Skipping.");
                if (customer != null)
                {
                    await CompletePendingFollowUpsAsync(dbContext, customer.Id);
                }
                return;
            }

            if (customer?.IsBlacklisted == true)
            {
                Console.WriteLine($"[AIReplyWorker] Customer {customer.Id} is blacklisted. Skipping AI reply.");
                await CompletePendingFollowUpsAsync(dbContext, customer.Id);
                return;
            }

            if (await HasReachedDailyAiReplyLimitAsync(dbContext, settings, channel))
            {
                Console.WriteLine($"[AIReplyWorker] Daily AI reply limit reached for project {@event.ProjectId}. Skipping AI generation for channel {channel}.");
                return;
            }

            if (customer != null)
            {
                var isPaid = await dbContext.GroupAppointmentBookings
                    .AnyAsync(b => b.CustomerId == customer.Id && b.IsPaid);
                if (isPaid)
                {
                    Console.WriteLine($"[AIReplyWorker] Customer {customer.Id} ({customer.PhoneNumber}) has a paid booking. Skipping AI reply.");
                    await CompletePendingFollowUpsAsync(dbContext, customer.Id);
                    return;
                }
            }

            // Intercept Messenger message for phone number transition
            if (channel == "Messenger" && customer != null)
            {
                var extractedPhone = EgyptianPhoneNumber.Extract(@event.Content);
                if (!string.IsNullOrEmpty(extractedPhone))
                {
                    string pageId = null;
                    string senderPSID = null;
                    if (!string.IsNullOrEmpty(@event.ChannelMetadata))
                    {
                        try
                        {
                            using var metaDoc = JsonDocument.Parse(@event.ChannelMetadata);
                            var metaRoot = metaDoc.RootElement;
                            pageId = metaRoot.TryGetProperty("pageId", out var pProp) ? pProp.GetString() : null;
                            senderPSID = metaRoot.TryGetProperty("senderPSID", out var sProp) ? sProp.GetString() : null;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[AIReplyWorker] Failed to parse ChannelMetadata: {ex.Message}");
                        }
                    }

                    if (string.IsNullOrEmpty(pageId)) pageId = @event.Sender;
                    if (string.IsNullOrEmpty(senderPSID)) senderPSID = @event.Sender;

                    var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub>>();

                    await HandleMessengerToWhatsAppTransitionAsync(
                        dbContext,
                        configuration,
                        hubContext,
                        scope.ServiceProvider,
                        customer,
                        extractedPhone,
                        settings,
                        pageId,
                        senderPSID);

                    return;
                }
            }

            // Decide which API key to use. Per-project key, or fall back to system default.
            string apiKeyOverride = !string.IsNullOrEmpty(settings.GeminiApiKey) ? settings.GeminiApiKey : null;

            string brainContext = null;
            string cachedContentId = null;
            string tonePref = !string.IsNullOrEmpty(aiBehaviorSettings.Tone.CustomTone)
                ? aiBehaviorSettings.Tone.CustomTone
                : (!string.IsNullOrEmpty(settings.AiTonePreference) ? settings.AiTonePreference : "العامية المصرية الروشة والصايعة");
            string targetAud = !string.IsNullOrEmpty(aiBehaviorSettings.Tone.TargetAudience)
                ? aiBehaviorSettings.Tone.TargetAudience
                : (!string.IsNullOrEmpty(settings.AiTargetAudience) ? settings.AiTargetAudience : "طلاب كورس كول سنتر يبحثون عن عمل");

            try
            {
                // Fetch all approved knowledge base chunks
                var approvedChunksList = await dbContext.KnowledgeChunks
                    .Include(c => c.KnowledgeDocument)
                    .Where(c => c.KnowledgeDocument!.ProjectId == @event.ProjectId &&
                                (c.KnowledgeDocument.Status == "Published" || c.KnowledgeDocument.Status == "Approved"))
                    .OrderBy(c => c.Id)
                    .Select(c => c.ChunkText)
                    .ToListAsync();

                var approvedChunksText = string.Join("\n\n", approvedChunksList.Select(text => $"- {text}"));
                var agentName = aiBehaviorSettingsService.GetAgentName(aiBehaviorSettings);
                var staticPrompt = _aiMarketingBrain.BuildStaticPrompt(agentName, tonePref, targetAud, approvedChunksText, systemPromptForReply, aiBehaviorSettings, channel);

                var geminiClient = scope.ServiceProvider.GetRequiredService<Modules.AI.Services.IGeminiClient>();
                int staticTokensCount = Math.Max(1, staticPrompt.Length / 4);
                Console.WriteLine($"[AIReplyWorker] Project {@event.ProjectId} estimated static prompt token count: {staticTokensCount}");

                if (staticTokensCount >= 32768)
                {
                    // Compute MD5 hash of staticPrompt
                    string contentHash;
                    using (var md5 = System.Security.Cryptography.MD5.Create())
                    {
                        byte[] hashBytes = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(staticPrompt));
                        contentHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    }

                    try
                    {
                        var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
                        string redisKey = $"gemini:cache:{@event.ProjectId}:{settings.GeminiModel}:{contentHash}";
                        cachedContentId = await redis.StringGetAsync(redisKey);

                        if (string.IsNullOrEmpty(cachedContentId))
                        {
                            Console.WriteLine($"[AIReplyWorker] Context cache not found/expired in Redis. Creating new cache on Gemini API...");
                            // Create cache with 3600 seconds (1 hour) TTL
                            cachedContentId = await geminiClient.CreateContextCacheAsync(staticPrompt, settings.GeminiModel, 3600, apiKeyOverride);
                            
                            // Store in Redis for 55 minutes
                            await redis.StringSetAsync(redisKey, cachedContentId, TimeSpan.FromMinutes(55));
                            Console.WriteLine($"[AIReplyWorker] Successfully cached static context. ID: {cachedContentId}");
                        }
                        else
                        {
                            Console.WriteLine($"[AIReplyWorker] Found active Context Cache in Redis: {cachedContentId}");
                        }
                    }
                    catch (Exception cacheEx)
                    {
                        Console.WriteLine($"[AIReplyWorker] Error managing context cache: {cacheEx.Message}. Falling back to standard RAG...");
                        cachedContentId = null;
                    }
                }

                if (string.IsNullOrEmpty(cachedContentId))
                {
                    // Fallback: Retrieve matching context from the Company Brain (Knowledge Base) via RAG search
                    var companyBrain = scope.ServiceProvider.GetRequiredService<Modules.Brain.Services.IAICompanyBrain>();
                    var chunks = await companyBrain.SearchBrainAsync(@event.ProjectId, @event.Content, limit: 3);
                    
                    var allChunks = new System.Collections.Generic.List<Modules.Brain.Services.KnowledgeChunkSearchDto>();
                    if (chunks != null)
                    {
                        allChunks.AddRange(chunks);
                    }

                    // Explicitly pull pricing and location chunks as guards to prevent AI hallucination
                    try
                    {
                        var pricingAndLocationChunks = await dbContext.KnowledgeChunks
                            .Include(c => c.KnowledgeDocument)
                            .Where(c => c.KnowledgeDocument!.ProjectId == @event.ProjectId &&
                                        (c.KnowledgeDocument.Status == "Published" || c.KnowledgeDocument.Status == "Approved") &&
                                        (c.ChunkText.Contains("الاشتراك الشهري") || c.ChunkText.Contains("عرض الكاش") || c.ChunkText.Contains("رابط اللوكيشن")))
                            .ToListAsync();

                        foreach (var pChunk in pricingAndLocationChunks)
                        {
                            if (!allChunks.Any(c => c.ChunkId == pChunk.Id))
                            {
                                allChunks.Add(new Modules.Brain.Services.KnowledgeChunkSearchDto
                                {
                                    ChunkId = pChunk.Id,
                                    DocumentId = pChunk.KnowledgeDocumentId,
                                    ChunkText = pChunk.ChunkText,
                                    SimilarityScore = 1.0
                                });
                            }
                        }
                    }
                    catch (Exception guardEx) when (guardEx is not System.Data.Common.DbException && !guardEx.ToString().Contains("EntityFrameworkCore"))
                    {
                        _logger.LogWarning(guardEx, "Failed to query pricing/location chunks");
                    }

                    if (allChunks.Any())
                    {
                        brainContext = string.Join("\n\n", allChunks.Select(c => $"- {c.ChunkText}"));
                        Console.WriteLine($"[AIReplyWorker] Injected {allChunks.Count} knowledge chunks (with pricing/location guards) into AI prompt context.");
                    }
                }
            }
            catch (Exception ex) when (ex is not System.Data.Common.DbException && !ex.ToString().Contains("EntityFrameworkCore"))
            {
                _logger.LogWarning(ex, "Failed to query company brain or process context cache");
            }

            string bookedGroupInfo = null;

            // Inject Group Appointments context if enabled
            if (settings.IsGroupAppointmentsEnabled)
            {
                try
                {
                    var activeGroups = await dbContext.GroupAppointments
                        .Include(g => g.Bookings)
                        .Where(g => g.ProjectId == @event.ProjectId && g.IsActive)
                        .OrderBy(g => g.DateTime)
                        .ToListAsync();

                    var groupsContextList = new System.Collections.Generic.List<string>();
                    TimeZoneInfo projectZone = TimezoneHelper.GetTimeZone(settings.Timezone);

                    // Filter out full groups vs available groups
                    var availableGroups = activeGroups.Where(g => g.Bookings.Count < g.Capacity).ToList();
                    var fullGroups = activeGroups.Where(g => g.Bookings.Count >= g.Capacity).ToList();

                    // Determine customer's city status and filter/adjust instructions accordingly
                    var customerCity = customer?.City?.Trim();
                    bool isCityKnown = !string.IsNullOrEmpty(customerCity) && !customerCity.Equals("Missing", StringComparison.OrdinalIgnoreCase);
                    bool isFromAlexandria = false;
                    if (isCityKnown)
                    {
                        var lowerCity = customerCity.ToLowerInvariant();
                        if (lowerCity.Contains("اسكندرية") || lowerCity.Contains("إسكندرية") || lowerCity.Contains("alexandria"))
                        {
                            isFromAlexandria = true;
                        }
                    }

                    // If customer is known and NOT from Alexandria, filter out offline (in center) groups completely
                    if (isCityKnown && !isFromAlexandria)
                    {
                        availableGroups = availableGroups.Where(g => g.Mode == "online").ToList();
                        fullGroups = fullGroups.Where(g => g.Mode == "online").ToList();
                    }

                    Console.WriteLine($"[AIReplyWorker] Active groups: {activeGroups.Count}, Available: {availableGroups.Count}, Full: {fullGroups.Count}, CityKnown: {isCityKnown}, FromAlexandria: {isFromAlexandria}");

                    string GetArabicDayName(DayOfWeek day)
                    {
                        switch (day)
                        {
                            case DayOfWeek.Sunday: return "الأحد";
                            case DayOfWeek.Monday: return "الاثنين";
                            case DayOfWeek.Tuesday: return "الثلاثاء";
                            case DayOfWeek.Wednesday: return "الأربعاء";
                            case DayOfWeek.Thursday: return "الخميس";
                            case DayOfWeek.Friday: return "الجمعة";
                            case DayOfWeek.Saturday: return "السبت";
                            default: return string.Empty;
                        }
                    }

                    string GetArabicDaysText(string daysCsv)
                    {
                        if (string.IsNullOrWhiteSpace(daysCsv))
                            return string.Empty;

                        var daysParts = daysCsv.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        var dayNames = new System.Collections.Generic.List<string>();
                        foreach (var part in daysParts)
                        {
                            if (int.TryParse(part.Trim(), out int dayIdx))
                            {
                                switch (dayIdx)
                                {
                                    case 0: dayNames.Add("الأحد"); break;
                                    case 1: dayNames.Add("الاثنين"); break;
                                    case 2: dayNames.Add("الثلاثاء"); break;
                                    case 3: dayNames.Add("الأربعاء"); break;
                                    case 4: dayNames.Add("الخميس"); break;
                                    case 5: dayNames.Add("الجمعة"); break;
                                    case 6: dayNames.Add("السبت"); break;
                                }
                            }
                        }
                        if (dayNames.Count == 0)
                            return string.Empty;
                        if (dayNames.Count == 1)
                            return "يوم " + dayNames[0];
                        if (dayNames.Count == 2)
                            return "يومي " + dayNames[0] + " و " + dayNames[1];
                        return "أيام " + string.Join(" و ", dayNames);
                    }

                    string GetArabicDateTimeText(DateTime? utcDateTime)
                    {
                        if (!utcDateTime.HasValue)
                            return string.Empty;

                        var utcTimeValue = DateTime.SpecifyKind(utcDateTime.Value, DateTimeKind.Utc);
                        var localTimeValue = TimeZoneInfo.ConvertTimeFromUtc(utcTimeValue, projectZone);
                        var dateTextValue = $"{GetArabicDayName(localTimeValue.DayOfWeek)} {localTimeValue:d/M}";
                        return $"{dateTextValue} الساعة {localTimeValue:h:mm} {(localTimeValue.Hour >= 12 ? "مساءً" : "صباحاً")}";
                    }

                    string BuildGroupScheduleDetails(GroupAppointment group)
                    {
                        var details = new System.Collections.Generic.List<string>();
                        var instructorName = string.IsNullOrWhiteSpace(group.InstructorName) ? "الإنستراكتور المسؤول عن المجموعة" : group.InstructorName.Trim();
                        var firstCourseSession = GetArabicDateTimeText(group.DateTime);
                        var secondCourseSession = GetArabicDateTimeText(group.CourseSecondDateTime);
                        var freeSession = GetArabicDateTimeText(group.FreeSessionDateTime);

                        if (!string.IsNullOrEmpty(group.InstructorName))
                        {
                            details.Add($"إنستراكتور الكورس: {instructorName}");
                        }
                        if (!string.IsNullOrEmpty(freeSession))
                        {
                            details.Add($"ميعاد السيشن المجانية: {freeSession} مع دكتور مصطفى");
                        }
                        if (!string.IsNullOrEmpty(firstCourseSession))
                        {
                            details.Add($"ميعاد السيشن الأولى للكورس: {firstCourseSession} مع {instructorName}");
                        }
                        if (!string.IsNullOrEmpty(secondCourseSession))
                        {
                            details.Add($"ميعاد السيشن الثانية للكورس: {secondCourseSession} مع {instructorName}");
                        }

                        return details.Count == 0 ? string.Empty : "\n  " + string.Join("\n  ", details);
                    }

                    foreach (var g in availableGroups)
                    {
                        // Convert the database UTC datetime back to the project's local timezone
                        var utcTime = DateTime.SpecifyKind(g.DateTime, DateTimeKind.Utc);
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, projectZone);
                        var modeText = g.Mode == "online" ? "أونلاين (Online)" : "في السنتر (Offline)";
                        var daysText = GetArabicDaysText(g.Days);
                        var daysLine = string.IsNullOrEmpty(daysText) ? "" : $"\n  أيام الموعد: {daysText}";
                        var dateText = $"{GetArabicDayName(localTime.DayOfWeek)} {localTime:d/M}";
                        var scheduleDetails = BuildGroupScheduleDetails(g);
                        groupsContextList.Add($"- معرف المجموعة (ID): {g.Id}\n  نوع المجموعة: {modeText}{daysLine}\n  تاريخ المجموعة الأساسي: {dateText}\n  الموعد الأساسي: الساعة {localTime:h:mm} {(localTime.Hour >= 12 ? "مساءً" : "صباحاً")}{scheduleDetails}\n  عدد المشتركين المسجلين حالياً: {g.Bookings.Count} من أصل {g.Capacity}");
                    }

                    var fullGroupsContextList = new System.Collections.Generic.List<string>();
                    foreach (var g in fullGroups)
                    {
                        var utcTime = DateTime.SpecifyKind(g.DateTime, DateTimeKind.Utc);
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, projectZone);
                        var modeText = g.Mode == "online" ? "أونلاين (Online)" : "في السنتر (Offline)";
                        var daysText = GetArabicDaysText(g.Days);
                        var daysLine = string.IsNullOrEmpty(daysText) ? "" : $"\n  أيام الموعد: {daysText}";
                        var dateText = $"{GetArabicDayName(localTime.DayOfWeek)} {localTime:d/M}";
                        var scheduleDetails = BuildGroupScheduleDetails(g);
                        fullGroupsContextList.Add($"- معرف المجموعة (ID): {g.Id}\n  نوع المجموعة: {modeText}{daysLine}\n  تاريخ المجموعة الأساسي: {dateText}\n  الموعد الأساسي: الساعة {localTime:h:mm} {(localTime.Hour >= 12 ? "مساءً" : "صباحاً")} (مكتملة العدد تماماً - ممتلئة){scheduleDetails}\n  عدد المشتركين المسجلين حالياً: {g.Bookings.Count} من أصل {g.Capacity}");
                    }

                    // Check if this customer is already booked in any group
                    GroupAppointment bookedGroup = null;
                    if (customer != null)
                    {
                        var booking = await dbContext.GroupAppointmentBookings
                            .Include(b => b.GroupAppointment)
                            .FirstOrDefaultAsync(b => b.ProjectId == @event.ProjectId && (b.CustomerId == customer.Id || b.CustomerPhone == @event.Sender));
                        if (booking != null)
                        {
                            bookedGroup = booking.GroupAppointment;
                        }
                    }

                    string alreadyBookedNote = "";
                    if (bookedGroup != null)
                    {
                        var utcTime = DateTime.SpecifyKind(bookedGroup.DateTime, DateTimeKind.Utc);
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, projectZone);
                        var modeText = bookedGroup.Mode == "online" ? "أونلاين (Online)" : "في السنتر (Offline)";
                        var bookedArabicDay = GetArabicDayName(localTime.DayOfWeek);
                        var bookedDateText = $"{bookedArabicDay} {localTime:d/M}";
                        var timeText = $"الساعة {localTime:h:mm} {(localTime.Hour >= 12 ? "مساءً" : "صباحاً")}";
                        var scheduleInfo = $"مجموعة {modeText} ({bookedDateText} {timeText})";
                        var bookedScheduleDetails = BuildGroupScheduleDetails(bookedGroup);

                        bookedGroupInfo = $"Group Name: {bookedGroup.Name}\nGroup ID: {bookedGroup.Id}\nSchedule: {modeText} ({bookedDateText} at {timeText}){bookedScheduleDetails}";

                        alreadyBookedNote = $"\nملاحظة هامة جداً وصارمة: العميل مسجل حالياً ومحجوز في الموعد التالي: {scheduleInfo} (اسم المجموعة: {bookedGroup.Name}، معرف المجموعة ID: {bookedGroup.Id})." +
                                            $"\nتفاصيل مواعيده الحالية عند السؤال عنها أو عند تأكيد الحجز:{bookedScheduleDetails}" +
                                            $"\n- إذا سأل العميل عن موعده أو مجموعته أو متى تم حجزه، أخبره بدقة وصراحة تامة بالموعد الحالي المحجوز فيه وتفاصيل السيشن المجانية وسيشني الكورس إذا كانت موجودة (ولا تخمن أو تخترع أي موعد آخر من المجموعات المتاحة!)." +
                                            $"\n- إذا طلب تغيير موعد المجموعة أو حجز مجموعة أخرى مختلفة، فقم بتسجيله في المجموعة الجديدة بوضع suggestedGroupBookingId = معرف المجموعة الجديد (ID). وسيقوم النظام بنقله تلقائياً." +
                                            $"\n- أما إذا سأل أو طلب الحجز في نفس مجموعته الحالية، أخبره بلطف أنه مسجل ومحجوز بالفعل في هذا الموعد ولا تسجله مرة أخرى (اترك suggestedGroupBookingId = null).";
                    }
                    string cityInstruction = "";
                    if (!isCityKnown)
                    {
                        cityInstruction = "قانون هام وصارم بشأن مدينة العميل وموقع المجموعات:\n" +
                                          "بما أن مدينة العميل غير مسجلة في ملفه الشخصي (City: Missing)، يجب عليك أولاً معرفة المدينة أو المحافظة التي يعيش فيها قبل تقديم أي مواعيد للعميل.\n" +
                                          "إذا سأل العميل عن المواعيد أو المجموعات أو تفاصيل الحجز، لا تذكر له أي مواعيد أو أوقات في ردك إطلاقاً، بل اسأله بلطف أولاً عن أين يعيش أو ما هي محافظته (مثال: 'علشان ننسق المواعيد المناسبة لحضرتك، ساكن في الإسكندرية ولا محافظة تانية؟').\n" +
                                          "يُمنع منعاً باتاً عرض المواعيد أو ذكرها للعميل إلا بعد أن يخبرك صراحةً بمدينته.\n";
                    }
                    else if (!isFromAlexandria)
                    {
                        cityInstruction = $"قانون هام وصارم بشأن مدينة العميل وموقع المجموعات:\n" +
                                          $"بما أن العميل يعيش في مدينة ({customerCity}) وهي ليست الإسكندرية، يُمنع منعاً باتاً عرض أو ذكر مواعيد 'في السنتر (Offline)' للعميل.\n" +
                                          $"اعرض عليه مواعيد 'أونلاين (Online)' المتاحة فقط، ولا تأتي على ذكر السنتر أو المواعيد الأوفلاين إطلاقاً في حديثك.\n";
                    }
                    else
                    {
                        cityInstruction = "قانون هام وصارم بشأن مدينة العميل وموقع المجموعات:\n" +
                                          "بما أن العميل يعيش في الإسكندرية، يمكنك عرض المجموعات المتاحة 'أونلاين (Online)' و'في السنتر (Offline)' معاً وخيره بينهما.\n";
                    }

                    var bookingPhoneInstruction =
                        NormalizeBookingPhone(customer?.PhoneNumber) == null
                            ? "قانون إلزامي لرقم صاحب الحجز:\n" +
                              "رقم الموبايل الحقيقي للشخص الذي يرسل الرسائل غير مسجل بعد. إذا كان يريد الحجز لنفسه، لا تضع suggestedGroupBookingId ولا تؤكد أن حجزه تم، واطلب رقم موبايله أولاً. أما إذا كان يحجز فقط لشخص آخر وقد أعطاك اسم هذا الشخص ورقمه الحقيقيين، فيمكنك حجز الشخص الآخر وحده داخل suggestedGroupBookingPeople مع isRequester=false. أي username أو @lid أو Messenger ID هو معرف داخلي وليس رقم هاتف، ويُمنع استخدامه أو ذكره كرقم للحجز.\n"
                            : string.Empty;

                    var groupsContextText = "معلومات مواعيد المجموعات المتاحة للحجز (Group Appointments):\n" +
                                            "إذا سأل العميل عن المجموعات أو المواعيد المتاحة أو يرغب في الحجز، اعرض عليه المجموعات المتاحة المناسبة له مع توضيح نوع كل مجموعة (سواء كانت أونلاين أو في السنتر)، وأيام الكورس، وميعاد السيشن المجانية، وميعادي سيشني الكورس الأسبوعيين، واسم الإنستراكتور المسؤول. السيشن المجانية سيشن واحدة مع دكتور مصطفى، والكورس عبارة عن سيشنين في الأسبوع مع إنستراكتور المجموعة. لا تذكر تفاصيل السيشن المجانية وسيشني الكورس في كل رد عادي؛ اذكرها فقط عند الحديث عن المواعيد أو الحجز أو قبل تأكيد الحجز أو إذا سأل العميل عنها. قبل تأكيد الحجز ذكّر العميل يتأكد أن ميعاد السيشن المجانية مناسب له وأن ميعادي الكورس الأسبوعيين مناسبين له. لا تذكر أبداً عدد الأماكن المتبقية أو السعة أو أي أرقام. إذا أراد الحجز في مجموعة محددة،ضع suggestedGroupBookingId = معرف المجموعة (ID) وأكد له الحجز في ردك. النظام سيسجله تلقائياً. لا ترسل أي رابط حجز للعميل. إذا لم تكن هناك مجموعات متاحة، أخبره أن المجموعات مكتملة حالياً.\n" +
                                            "تنبيه هام جداً وصارم بشأن توافر المجموعات وسعتها: إذا كانت المجموعة مدرجة في 'قائمة المجموعات المتاحة حالياً' بالأسفل، فهذا يعني بشكل قاطع وبقوة النظام أنها متاحة وبها أماكن شاغرة ومفتوحة للحجز الفعلي والمباشر. تجاهل تماماً أي معلومات قديمة أو متعارضة في القاعدة المعرفية أو الملفات المرفقة (مثل التي تدعي أن مجموعات السنتر/سيدي جابر مكتملة تماماً، أو تدعي أن المجموعات الأونلاين مكتملة، أو تطلب تسجيل العملاء في 'قائمة الانتظار'، أو تحدد سعة معينة للمجموعات الأونلاين أو الأوفلاين مثل 12 إلى 20 أو 21). اعتمد فقط وحصرياً على أرقام المشتركين والسعة الموضحة في القائمة بالأسفل (مثلاً إذا كان عدد المشتركين الحالي 41 من أصل 60، فهذا يعني أن هناك 19 مكاناً شاغراً، وبالتالي المجموعة ليست كاملة بل مفتوحة للحجز الفوري). يُمنع منعاً باتاً ذكر 'قائمة الانتظار' للعميل أو الادعاء بأن المجموعات مكتملة طالما أن المجموعة تظهر في القائمة بالأسفل؛ بل احجز للعميل فيها مباشرة وبشكل طبيعي إذا رغب في ذلك، مع الالتزام التام بعدم ذكر العدد أو السعة أو أي أرقام أو إحصائيات للحجز للعميل إطلاقاً (مثل لا تقل له 'متبقي 19 مكان' أو 'العدد الحالي 41/60'، بل قل له فقط 'سجلتك في المجموعة' أو 'المجموعة متاحة للحجز').\n" +
                                            bookingPhoneInstruction +
                                            cityInstruction + "\n" +
                                            alreadyBookedNote + "\n\n" +
                                            "قائمة المجموعات المتاحة حالياً:\n" +
                                            (groupsContextList.Any() ? string.Join("\n", groupsContextList) : "- لا توجد مجموعات متاحة حالياً للحجز.") + "\n\n" +
                                            "قائمة المجموعات المكتملة العدد حالياً (كاملة العدد ويُمنع الحجز فيها تماماً):\n" +
                                            (fullGroupsContextList.Any() ? string.Join("\n", fullGroupsContextList) : "- لا توجد مجموعات مكتملة العدد حالياً.") + "\n\n" +
                                            "قوانين صارمة جداً بشأن حضور المجموعات والمجموعات المكتملة:\n" +
                                            "1. المجموعات الأونلاين (Online) مخصصة فقط وحصرياً للحضور عبر الإنترنت. يُمنع منعاً باتاً إخبار أو إيحاء طلاب الأونلاين بإمكانية الحضور في السنتر/أوفلاين. يجب التأكيد التام عليهم أن حضورهم أونلاين فقط ولا يجوز حضورهم في السنتر.\n" +
                                            "2. المجموعات في السنتر (Offline) مخصصة فقط وحصرياً للحضور الفعلي الجسدي داخل السنتر. ولا يوجد لها حضور أونلاين.\n" +
                                            "3. المجموعات المكتملة العدد (ممتلئة) هي مجموعات موجودة بالفعل في النظام ولكنها ممتلئة تماماً. إذا سأل العميل عنها، أخبره صراحةً أنها مكتملة العدد وممتلئة حالياً، ولكن لا تقل له أنها غير موجودة أو لم تفتح بعد. يُمنع منعاً باتاً حجز العميل في مجموعة مكتملة العدد (أي لا تضع suggestedGroupBookingId لها).\n";

                    if (string.IsNullOrEmpty(brainContext))
                    {
                        brainContext = groupsContextText;
                    }
                    else
                    {
                        brainContext = groupsContextText + "\n\n" + brainContext;
                    }
                    Console.WriteLine($"[AIReplyWorker] Injected Group Appointments context (Found {activeGroups.Count} active, Available: {availableGroups.Count}, Full: {fullGroups.Count}).");
                }
                catch (Exception ex) when (ex is not System.Data.Common.DbException && !ex.ToString().Contains("EntityFrameworkCore"))
                {
                    _logger.LogWarning(ex, "Failed to query active group appointments for AI context");
                }
            }

            // 1. WhatsApp session number fetching and direct redirect instructions
            string whatsappLinkContext = "";
            try
            {
                gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
                using var httpClientObj = new System.Net.Http.HttpClient();
                var gatewayResponse = await httpClientObj.GetAsync($"{gatewayUrl}/api/whatsapp/session/status?projectId={@event.ProjectId}");
                if (gatewayResponse.IsSuccessStatusCode)
                {
                    var responseBody = await gatewayResponse.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
                    if (doc.RootElement.TryGetProperty("phoneNumber", out var phoneProp) && !string.IsNullOrEmpty(phoneProp.GetString()))
                    {
                        var phoneNum = phoneProp.GetString();
                        whatsappLinkContext = $"\n[معلومات رقم التواصل وواتساب المشروع]:\n" +
                                              $"- رقم الواتساب الخاص بالصفحة/المشروع هو: {phoneNum}\n" +
                                              $"- رابط الواتساب المباشر للتواصل هو: https://wa.me/{phoneNum}\n" +
                                              $"توجيه صارم للـ AI: إذا طلب العميل رقم الهاتف للتواصل، أو سألك عن كيفية التواصل عبر الواتساب أو طلب رقم الواتساب، فيُمنع تماماً تخمين أو كتابة أي رقم آخر. يجب عليك قاطعاً إرسال هذا الرقم المذكور أعلاه ({phoneNum}) وإرسال رابط الواتساب المباشر المذكور (https://wa.me/{phoneNum}) لكي ينقر عليه ويتواصل معنا مباشرة.\n";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIReplyWorker] Failed to fetch WhatsApp status for project: {ex.Message}");
            }

            // 2. Channel awareness context
            string channelAwarenessContext = $"\n[قناة التواصل الحالية]: {(channel == "WhatsApp" ? "واتساب (WhatsApp)" : channel == "Messenger" ? "فيسبوك ماسنجر (Facebook Messenger)" : "تعليقات فيسبوك (Facebook Comment)")}\n" +
                                             $"توجيه هام وصارم للـ AI: أنت تقوم حالياً بالرد على العميل عبر قناة [{channel}]. يرجى صياغة وتنسيق ردك بما يتناسب مع هذه القناة تحديداً (على سبيل المثال: إذا كانت القناة تعليقاً على منشور، يرجى كتابة رد عام وموجز جداً يناسب التعليقات العامة، أما إذا كانت ماسنجر أو واتساب فيمكنك الرد بتفاصيل أوفى والترحيب بالعميل).\n";

            if ((channel == "Messenger" || channel == "WhatsApp") && aiBehaviorSettings.Cta.Enabled)
            {
                channelAwarenessContext += $"\nتوجيه CTA لقناة ({channel}):\n" +
                                           "- أضف CTA واحداً فقط عندما يكون مناسباً لآخر اهتمام واضح للعميل، وليس في كل رد. اختر موضوع CTA من الإعدادات، ولا تستخدم وعوداً أو عروضاً غير موجودة فيها.\n";

                if (channel == "Messenger" && (customer == null || string.IsNullOrEmpty(customer.PhoneNumber)))
                {
                    channelAwarenessContext += "- يجب عليك دائمًا وبأسلوب لطيف ومقنع (سيلزجي بالعامية المصرية) محاولة طلب رقم الواتساب الخاص بالعميل لنقل المحادثة إلى الواتساب (مثال بالعامية: 'يا ريت تبعتلي رقم الواتساب بتاعك عشان نبعتلك التفاصيل عليه ونكمل كلامنا هناك').\n";
                }
            }

            brainContext = (brainContext ?? "") + whatsappLinkContext + channelAwarenessContext;

            if (customer != null && customer.IsBlacklisted)
            {
                Console.WriteLine($"[AIReplyWorker] Customer {@event.Sender} is blacklisted. Skipping AI reply.");
                await CompletePendingFollowUpsAsync(dbContext, customer.Id);
                return;
            }

            Guid customerId = customer?.Id ?? Guid.Empty;

            // Fetch chat history for context
            string chatHistory = null;
            Conversation conversation = null;
            if (customerId != Guid.Empty)
            {
                try
                {
                    conversation = await dbContext.Conversations
                        .FirstOrDefaultAsync(c => c.CustomerId == customerId && c.Channel == channel && c.Status != "Closed");

                    if (conversation != null)
                    {
                        var historyMessages = await dbContext.Messages
                            .Where(m => m.ConversationId == conversation.Id)
                            .OrderByDescending(m => m.Timestamp)
                            .Take(15) // Limit history to last 15 messages
                            .ToListAsync();

                        historyMessages.Reverse(); // Chronological order

                        chatHistory = string.Join("\n", historyMessages.Select(m => 
                            $"{(m.Direction == "Incoming" ? "Customer" : "Agent/AI")}: {m.Content}"));
                        
                        Console.WriteLine($"[AIReplyWorker] Injected {historyMessages.Count} history messages into AI prompt context.");
                    }
                }
                catch (Exception ex) when (ex is not System.Data.Common.DbException && !ex.ToString().Contains("EntityFrameworkCore"))
                {
                    _logger.LogWarning(ex, "Failed to query chat history");
                }
            }

            // Retrieve CustomerMemory
            string customerMemory = null;
            if (customerId != Guid.Empty)
            {
                try
                {
                    var memory = await dbContext.CustomerMemories
                        .FirstOrDefaultAsync(m => m.CustomerId == customerId);
                    if (memory != null)
                    {
                        var summaryText = memory.LongTermSummary;
                        var factsText = string.IsNullOrEmpty(memory.FactsJson) || memory.FactsJson == "[]"
                            ? ""
                            : "\nFacts: " + string.Join(", ", System.Text.Json.JsonSerializer.Deserialize<string[]>(memory.FactsJson));
                        var objectionsText = string.IsNullOrEmpty(memory.ObjectionsJson) || memory.ObjectionsJson == "[]"
                            ? ""
                            : "\nObjections: " + string.Join(", ", System.Text.Json.JsonSerializer.Deserialize<string[]>(memory.ObjectionsJson));

                        customerMemory = $"Summary: {summaryText}{factsText}{objectionsText}";
                        Console.WriteLine($"[AIReplyWorker] Injected Customer Memory: {customerMemory}");
                    }
                }
                catch (Exception ex) when (ex is not System.Data.Common.DbException && !ex.ToString().Contains("EntityFrameworkCore"))
                {
                    _logger.LogWarning(ex, "Failed to query customer memory");
                }
            }

            // Fetch existing customer labels to restrict options
            string[] existingLabels = Array.Empty<string>();
            try
            {
                existingLabels = await dbContext.Customers
                    .Where(c => c.ProjectId == @event.ProjectId && c.Label != null && c.Label != "")
                    .Select(c => c.Label)
                    .Distinct()
                    .ToArrayAsync();
            }
            catch (Exception ex) when (ex is not System.Data.Common.DbException && !ex.ToString().Contains("EntityFrameworkCore"))
            {
                _logger.LogWarning(ex, "Failed to query existing labels");
            }

            // Construct customer profile description to probe for missing data
            string customerProfile = $"Name: {(string.IsNullOrEmpty(customer?.Name) ? "Missing" : customer.Name)}\n" +
                                     $"City: {(string.IsNullOrEmpty(customer?.City) ? "Missing" : customer.City)}";
            if (!string.IsNullOrEmpty(bookedGroupInfo))
            {
                customerProfile += $"\nCurrent Booking:\n{bookedGroupInfo}";
            }

            // Check for media attachments in the active conversation
            byte[] fileBytes = null;
            string mimeType = null;
            Message latestMediaMsg = null;

            if (conversation != null)
            {
                latestMediaMsg = await dbContext.Messages
                    .Where(m => m.ConversationId == conversation.Id && m.Direction == "Incoming" && m.AssetId != null)
                    .OrderByDescending(m => m.Timestamp)
                    .FirstOrDefaultAsync();

                if (latestMediaMsg != null)
                {
                    var timeDiff = DateTime.UtcNow - latestMediaMsg.Timestamp;
                    if (timeDiff.TotalMinutes <= 2.0)
                    {
                        var asset = await dbContext.Assets
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(a => a.Id == latestMediaMsg.AssetId);

                        if (asset != null)
                        {
                            try
                            {
                                var storageService = scope.ServiceProvider.GetRequiredService<Modules.Media.Services.IMinIoStorageService>();
                                using var stream = await storageService.DownloadFileAsync(asset.StoragePath);
                                using var ms = new System.IO.MemoryStream();
                                await stream.CopyToAsync(ms);
                                fileBytes = ms.ToArray();
                                mimeType = asset.ContentType;
                                Console.WriteLine($"[AIReplyWorker] Downloaded multimodal media: {asset.FileName} ({fileBytes.Length} bytes) of type {mimeType}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[AIReplyWorker] Failed to download media asset from MinIO: {ex.Message}");
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"[AIReplyWorker] Generating AI response using AIMarketingBrain...");
            var analysisResult = await _aiMarketingBrain.AnalyzeAndGenerateReplyAsync(
                @event.Content, 
                apiKeyOverride, 
                brainContext, 
                chatHistory, 
                customerMemory,
                existingLabels,
                customerProfile,
                fileBytes,
                mimeType,
                tonePref,
                targetAud,
                settings.GeminiModel,
                cachedContentId,
                systemPromptForReply,
                aiBehaviorSettings,
                channel);

            EnforceDistinctBookingPhones(customer, analysisResult);
            EnforceRequesterBookingPhoneRequirement(channel, customer, analysisResult);
            ApplyFollowUpPolicy(aiBehaviorSettings, analysisResult);
            await ApplyKnowledgePricingGuardAsync(dbContext, @event.ProjectId, @event.Content, analysisResult);
            if (!aiBehaviorSettingsService.IsReactionAllowed(aiBehaviorSettings, analysisResult.SuggestedReaction))
            {
                analysisResult.SuggestedReaction = null;
            }

            if (latestMediaMsg != null && !string.IsNullOrEmpty(analysisResult.Transcription))
            {
                latestMediaMsg.Transcription = analysisResult.Transcription;
                dbContext.Entry(latestMediaMsg).State = EntityState.Modified;
                await dbContext.SaveChangesAsync();
                Console.WriteLine($"[AIReplyWorker] Saved voice note transcription: {latestMediaMsg.Transcription}");
            }

            Console.WriteLine($"[AIReplyWorker] AI Response: {analysisResult.ReplyContent}");

            // 1. Publish CRM Update suggestion
            var crmSuggestion = new CRMUpdateSuggestedEvent
            {
                ProjectId = @event.ProjectId,
                CustomerId = customerId,
                Sender = @event.Sender,
                City = analysisResult.Entities?.City,
                Budget = analysisResult.Entities?.Budget,
                Interests = analysisResult.Entities?.Interests ?? Array.Empty<string>(),
                Timeline = analysisResult.Entities?.Timeline,
                Intent = analysisResult.Intent,
                Sentiment = analysisResult.Sentiment,
                Confidence = analysisResult.Confidence,
                Label = analysisResult.Label,
                PipelineStage = analysisResult.PipelineStage,
                FollowUpNeeded = analysisResult.SuggestedFollowUp?.Needed ?? false,
                FollowUpType = analysisResult.SuggestedFollowUp?.Type,
                FollowUpAppointmentTime = analysisResult.SuggestedFollowUp?.AppointmentTime,
                FollowUpDueDate = analysisResult.SuggestedFollowUp?.DueDate,
                FollowUpNotes = analysisResult.SuggestedFollowUp?.Notes,
                AIInsights = analysisResult.AIInsights
            };
            await _eventBus.PublishAsync(crmSuggestion);
            Console.WriteLine($"[AIReplyWorker] Published CRMUpdateSuggestedEvent for {@event.Sender}");

            var replyGeneratedEvent = new AIReplyGeneratedEvent
            {
                ProjectId = @event.ProjectId,
                Sender = @event.Sender,
                Content = analysisResult.ReplyContent,
                Buttons = analysisResult.SuggestedButtons ?? Array.Empty<string>(),
                Channel = @event.Channel ?? "WhatsApp",
                ChannelMetadata = @event.ChannelMetadata,
                Reaction = analysisResult.SuggestedReaction,
                PublicCommentReply = analysisResult.PublicCommentReply
            };

            await _eventBus.PublishAsync(replyGeneratedEvent);
            Console.WriteLine($"[AIReplyWorker] Published AIReplyGeneratedEvent for {@event.Sender}");

            // Intercept Human Request
            if (analysisResult.RequestHuman && settings.HumanTransferEnabled && !string.IsNullOrWhiteSpace(settings.HumanTransferPhone))
            {
                try
                {
                    var managerPhone = settings.HumanTransferPhone.Trim();
                    var customerName = customer?.Name ?? "عميل غير معروف";
                    var customerPhone = customer?.PhoneNumber ?? @event.Sender;

                    var managerMsg = $"العميل {customerName} ({customerPhone}) طلب التحدث مع شخص طبيعي.";
                    dbContext.NotificationAlerts.Add(new NotificationAlert
                    {
                        ProjectId = @event.ProjectId,
                        UserId = Guid.Empty,
                        Type = "HumanTransferRequest",
                        Message = managerMsg,
                        IsRead = false
                    });
                    await dbContext.SaveChangesAsync();

                    await SendWhatsAppTransitionMessageAsync(
                        gatewayUrl,
                        @event.ProjectId,
                        managerPhone,
                        "المدير",
                        "النظام",
                        managerMsg
                    );
                    Console.WriteLine($"[AIReplyWorker] Sent human request notification to manager: {managerPhone}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AIReplyWorker] Failed to send human request notification: {ex.Message}");
                }
            }

            // Intercept Blacklist Customer (attended & subscribed)
            if (analysisResult.BlacklistCustomer && customer != null)
            {
                try
                {
                    customer.IsBlacklisted = true;
                    dbContext.Entry(customer).State = EntityState.Modified;
                    await dbContext.SaveChangesAsync();
                    Console.WriteLine($"[AIReplyWorker] Automatically blacklisted customer {customer.Id} ({customer.PhoneNumber}) as they subscribed.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AIReplyWorker] Failed to auto-blacklist customer: {ex.Message}");
                }
            }

            // 2.5. Process AI Auto-Booking if suggestedGroupBookingId is set
            if (!string.IsNullOrEmpty(analysisResult.SuggestedGroupBookingId))
            {
                try
                {
                    if (Guid.TryParse(analysisResult.SuggestedGroupBookingId, out var groupId))
                    {
                        var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<Modules.Conversations.Hubs.NotificationHub>>();
                        await BookSuggestedPeopleAsync(new AutoBookingRequest
                        {
                            DbContext = dbContext,
                            HubContext = hubContext,
                            ProjectId = @event.ProjectId,
                            GroupId = groupId,
                            Requester = customer,
                            SuggestedPeople = analysisResult.SuggestedGroupBookingPeople,
                            Timezone = settings?.Timezone
                        });
                    }
                    else
                    {
                        Console.WriteLine($"[AIReplyWorker] Auto-booking failed: Invalid GUID '{analysisResult.SuggestedGroupBookingId}'.");
                    }
                }
                catch (Exception bookingEx) when (bookingEx is not System.Data.Common.DbException && !bookingEx.ToString().Contains("EntityFrameworkCore"))
                {
                    _logger.LogWarning(bookingEx, "Auto-booking error");
                }
            }

            // 2.6. Process AI Auto-Cancellation if CancelGroupBooking is set to true
            if (analysisResult.CancelGroupBooking)
            {
                try
                {
                    var bookingCustomerId = customer?.Id ?? Guid.Empty;
                    var bookingCustomerPhone = customer?.PhoneNumber ?? @event.Sender;

                    var existingBooking = await dbContext.GroupAppointmentBookings
                        .Include(b => b.GroupAppointment)
                        .FirstOrDefaultAsync(b => b.ProjectId == @event.ProjectId && (b.CustomerPhone == bookingCustomerPhone || b.CustomerId == bookingCustomerId));

                    if (existingBooking != null)
                    {
                        var groupName = existingBooking.GroupAppointment?.Name ?? "المجموعة";
                        var groupId = existingBooking.GroupAppointmentId;

                        dbContext.GroupAppointmentBookings.Remove(existingBooking);
                        
                        // Update customer notes to document the cancellation
                        if (customer != null)
                        {
                            TimeZoneInfo projectZone = TimezoneHelper.GetTimeZone(settings?.Timezone);
                            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone);
                            customer.Notes = (customer.Notes ?? string.Empty) + $"\nتم إلغاء حجز الطالب من مجموعة {groupName} (تلقائياً بالـ AI) بتاريخ {localTime:yyyy-MM-dd HH:mm}";
                            dbContext.Entry(customer).State = EntityState.Modified;
                        }

                        await dbContext.SaveChangesAsync();
                        Console.WriteLine($"[AIReplyWorker] ❌ Auto-cancelled booking for customer {bookingCustomerPhone} from group '{groupName}'.");

                        // Broadcast update via SignalR to refresh dashboard
                        try
                        {
                            var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub>>();
                            await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("GroupBookingUpdated", new
                            {
                                groupId = groupId,
                                groupName = groupName,
                                customerPhone = bookingCustomerPhone,
                                customerName = customer?.Name ?? bookingCustomerPhone,
                                newBookedCount = existingBooking.GroupAppointment != null ? Math.Max(0, existingBooking.GroupAppointment.Bookings.Count - 1) : 0,
                                isCancelled = true
                            });
                        }
                        catch (Exception signalREx)
                        {
                            Console.WriteLine($"[AIReplyWorker] SignalR broadcast for group booking cancellation failed: {signalREx.Message}");
                        }
                    }
                }
                catch (Exception cancelEx)
                {
                    Console.WriteLine($"[AIReplyWorker] Auto-cancellation failed: {cancelEx.Message}");
                }
            }

            // 3. Process AI Auto-Reaction if suggested (WhatsApp only)
            if (channel == "WhatsApp" && aiBehaviorSettingsService.IsReactionAllowed(aiBehaviorSettings, analysisResult.SuggestedReaction))
            {
                try
                {
                    if (conversation != null)
                    {
                        var targetMessage = await dbContext.Messages
                            .Where(m => m.ConversationId == conversation.Id && m.Direction == "Incoming")
                            .OrderByDescending(m => m.Timestamp)
                            .FirstOrDefaultAsync();

                        if (targetMessage != null)
                        {
                            var reactionMessage = new Message
                            {
                                ConversationId = conversation.Id,
                                ExternalMessageId = $"msg_ai_react_{Guid.NewGuid().ToString("N")}",
                                Direction = "Outgoing",
                                Content = $"[تفاعل] {analysisResult.SuggestedReaction}",
                                MessageType = "Reaction",
                                Timestamp = DateTime.UtcNow
                            };

                            dbContext.Messages.Add(reactionMessage);
                            await dbContext.SaveChangesAsync();

                            gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";

                            var gatewayPayload = new
                            {
                                projectId = conversation.ProjectId,
                                to = @event.Sender,
                                reactionText = analysisResult.SuggestedReaction,
                                targetMessageId = targetMessage.ExternalMessageId,
                                targetFromMe = false
                            };

                            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(gatewayPayload, new System.Text.Json.JsonSerializerOptions 
                            { 
                                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase 
                            });

                            var httpClient = new System.Net.Http.HttpClient();
                            var gatewayResponse = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(httpClient, $"{gatewayUrl}/api/whatsapp/react", jsonPayload);
                            if (gatewayResponse.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"[AIReplyWorker] Sent reaction {analysisResult.SuggestedReaction} to message {targetMessage.ExternalMessageId}");
                            }
                            else
                            {
                                var body = await gatewayResponse.Content.ReadAsStringAsync();
                                Console.WriteLine($"[AIReplyWorker] Gateway reaction returned {gatewayResponse.StatusCode}: {body}");
                            }

                            // Broadcast via SignalR to project group
                            var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub>>();
                            var signalrPayload = new
                            {
                                id = reactionMessage.Id,
                                conversationId = reactionMessage.ConversationId,
                                senderType = "AI",
                                content = reactionMessage.Content,
                                createdAt = reactionMessage.Timestamp.ToString("o"),
                                status = "Sent",
                                mediaUrl = (string)null,
                                mediaType = (string)null,
                                messageType = "Reaction"
                            };

                            await hubContext.Clients.Group($"project_{conversation.ProjectId}").SendAsync("ReceiveMessage", signalrPayload);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AIReplyWorker] Failed to process auto-reaction: {ex.Message}");
                }
            }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIReplyWorker] CRITICAL ERROR IN AI REPLY PROCESS: {ex.Message}");
                try
                {
                    var customer = await dbContext.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == @event.Sender);
                    if (customer != null)
                    {
                        var conversation = await dbContext.Conversations
                            .IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.CustomerId == customer.Id && c.Channel == channel && c.Status != "Closed");

                        if (conversation != null)
                        {
                            try
                            {
                                var redis = scope.ServiceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
                                await redis.KeyDeleteAsync($"ai_typing:{conversation.Id}");
                            }
                            catch (Exception redisEx)
                            {
                                Console.WriteLine($"[AIReplyWorker] Redis delete on error failed: {redisEx.Message}");
                            }

                            var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub>>();
                            await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITyping", new
                            {
                                conversationId = conversation.Id,
                                isTyping = false
                            });

                            await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("AITypingError", new
                            {
                                conversationId = conversation.Id,
                                message = $"فشل الرد التلقائي للعميل {customer.Name ?? @event.Sender}: {ex.Message}"
                            });
                        }
                    }
                }
                catch (Exception handlerEx)
                {
                    Console.WriteLine($"[AIReplyWorker] Error handler failed: {handlerEx.Message}");
                }

                throw;
            }
        }

        private sealed class AutoBookingRequest
        {
            public required AppDbContext DbContext { get; init; }
            public required IHubContext<Modules.Conversations.Hubs.NotificationHub> HubContext { get; init; }
            public Guid ProjectId { get; init; }
            public Guid GroupId { get; init; }
            public Customer? Requester { get; init; }
            public SuggestedGroupBookingPerson[] SuggestedPeople { get; init; } = Array.Empty<SuggestedGroupBookingPerson>();
            public string? Timezone { get; init; }
        }

        private sealed class AutoBookingSession
        {
            public required AutoBookingRequest Request { get; init; }
            public required GroupAppointment Group { get; init; }
            public required DateTime LocalNow { get; init; }
            public int BookedCount { get; set; }
        }

        private sealed record BookingCandidate(string Name, string Phone, Customer? Customer);

        private async Task BookSuggestedPeopleAsync(AutoBookingRequest request)
        {
            var group = await FindActiveBookingGroupAsync(request);
            if (group == null) return;

            var projectZone = TimezoneHelper.GetTimeZone(request.Timezone);
            if (GroupIsExpired(group, projectZone))
            {
                await DeactivateGroupAsync(request, group);
                return;
            }
            var session = new AutoBookingSession
            {
                Request = request,
                Group = group,
                LocalNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone),
                BookedCount = group.Bookings.Count
            };

            foreach (var candidate in GetBookingCandidates(request))
            {
                await BookCandidateAsync(session, candidate);
            }
        }

        private async Task<GroupAppointment?> FindActiveBookingGroupAsync(AutoBookingRequest request)
        {
            var group = await request.DbContext.GroupAppointments
                .Include(groupAppointment => groupAppointment.Bookings)
                .FirstOrDefaultAsync(groupAppointment => groupAppointment.Id == request.GroupId && groupAppointment.ProjectId == request.ProjectId && groupAppointment.IsActive);
            if (group == null)
            {
                _logger.LogWarning("Auto-booking failed: active group {GroupId} was not found in project {ProjectId}.", request.GroupId, request.ProjectId);
            }
            return group;
        }

        private static bool GroupIsExpired(GroupAppointment group, TimeZoneInfo projectZone)
        {
            var localGroupTime = TimeZoneInfo.ConvertTimeFromUtc(group.DateTime, projectZone);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone);
            return (localNow - localGroupTime).TotalHours >= 24;
        }

        private async Task DeactivateGroupAsync(AutoBookingRequest request, GroupAppointment group)
        {
            group.IsActive = false;
            await request.DbContext.SaveChangesAsync();
            _logger.LogInformation("Deactivated expired group {GroupId} before AI auto-booking.", group.Id);
        }

        private IEnumerable<BookingCandidate> GetBookingCandidates(AutoBookingRequest request)
        {
            var people = request.SuggestedPeople.Length > 0
                ? request.SuggestedPeople
                : new[] { new SuggestedGroupBookingPerson { IsRequester = true } };
            var uniquePhones = new HashSet<string>(StringComparer.Ordinal);
            var requesterPhone = NormalizeBookingPhone(request.Requester?.PhoneNumber);

            foreach (var person in people)
            {
                var customer = person.IsRequester ? request.Requester : null;
                var phone = person.IsRequester
                    ? requesterPhone ?? NormalizeBookingPhone(person.PhoneNumber)
                    : NormalizeBookingPhone(person.PhoneNumber);
                var name = (person.IsRequester
                    ? person.Name?.Trim() ?? customer?.Name
                    : person.Name)?.Trim();
                var usesRequesterPhoneForOtherPerson = !person.IsRequester && phone == requesterPhone;
                if (phone != null && !usesRequesterPhoneForOtherPerson && !string.IsNullOrWhiteSpace(name) && uniquePhones.Add(phone))
                {
                    yield return new BookingCandidate(name, phone, customer);
                }
                else
                {
                    _logger.LogWarning("Skipped invalid or duplicate AI booking person in project {ProjectId}.", request.ProjectId);
                }
            }
        }

        private async Task BookCandidateAsync(AutoBookingSession session, BookingCandidate candidate)
        {
            var customer = candidate.Customer ?? await FindBookingCustomerAsync(session, candidate.Phone);
            var existingBooking = await FindExistingBookingAsync(session, candidate.Phone, customer?.Id);
            if (existingBooking?.GroupAppointmentId == session.Group.Id)
            {
                await RefreshExistingBookingAsync(session, candidate, existingBooking);
                return;
            }
            if (session.BookedCount >= session.Group.Capacity)
            {
                _logger.LogWarning("AI auto-booking stopped because group {GroupId} reached capacity.", session.Group.Id);
                return;
            }

            customer ??= await CreateBookingCustomerAsync(session, candidate);
            UpdateBookingCustomer(customer, session, candidate);
            var booking = BuildBooking(session, candidate, customer, existingBooking);
            if (existingBooking == null)
            {
                session.Request.DbContext.GroupAppointmentBookings.Add(booking);
                IntegrationOutbox.Enqueue(session.Request.DbContext, new AdvertisingBookingOutcomeChanged
                {
                    ProjectId = session.Request.ProjectId,
                    BookingId = booking.Id,
                    CustomerId = customer.Id,
                    IsPaid = booking.IsPaid,
                    IsAttended = booking.IsAttended,
                    Value = 0m,
                    Currency = "EGP"
                });
            }
            await session.Request.DbContext.SaveChangesAsync();
            session.BookedCount++;
            await BroadcastBookingAsync(session, candidate, booking);
        }

        private Task<Customer?> FindBookingCustomerAsync(AutoBookingSession session, string phone) =>
            session.Request.DbContext.Customers.FirstOrDefaultAsync(customer => customer.ProjectId == session.Request.ProjectId && customer.PhoneNumber == phone);

        private Task<GroupAppointmentBooking?> FindExistingBookingAsync(AutoBookingSession session, string phone, Guid? customerId) =>
            session.Request.DbContext.GroupAppointmentBookings.FirstOrDefaultAsync(booking =>
                booking.ProjectId == session.Request.ProjectId &&
                (booking.CustomerPhone == phone || (customerId.HasValue && booking.CustomerId == customerId.Value)));

        private async Task RefreshExistingBookingAsync(AutoBookingSession session, BookingCandidate candidate, GroupAppointmentBooking booking)
        {
            booking.CustomerName = candidate.Name;
            booking.CustomerPhone = candidate.Phone;
            await session.Request.DbContext.SaveChangesAsync();
            _logger.LogInformation("AI auto-booking skipped because {Phone} is already registered in group {GroupId}.", candidate.Phone, session.Group.Id);
        }

        private async Task<Customer> CreateBookingCustomerAsync(AutoBookingSession session, BookingCandidate candidate)
        {
            var customer = new Customer
            {
                ProjectId = session.Request.ProjectId,
                PhoneNumber = candidate.Phone,
                Name = candidate.Name,
                City = string.Empty,
                LeadScore = 10,
                Tags = new[] { "حجز مجموعة" },
                Notes = $"تم الحجز تلقائياً بواسطة عميل آخر في مجموعة: {session.Group.Name}"
            };
            session.Request.DbContext.Customers.Add(customer);
            await session.Request.DbContext.SaveChangesAsync();
            return customer;
        }

        private static void UpdateBookingCustomer(Customer customer, AutoBookingSession session, BookingCandidate candidate)
        {
            var tags = customer.Tags?.ToList() ?? new List<string>();
            if (!tags.Contains("حجز مجموعة")) tags.Add("حجز مجموعة");
            customer.Tags = tags.ToArray();
            customer.Name = candidate.Name;
            customer.Notes = (customer.Notes ?? string.Empty) +
                $"\nتم حجز موعد في مجموعة: {session.Group.Name} (تلقائياً بالـ AI) بتاريخ {session.LocalNow:yyyy-MM-dd HH:mm}";
        }

        private static GroupAppointmentBooking BuildBooking(
            AutoBookingSession session,
            BookingCandidate candidate,
            Customer customer,
            GroupAppointmentBooking? existingBooking)
        {
            var booking = existingBooking ?? new GroupAppointmentBooking { Id = Guid.NewGuid(), ProjectId = session.Request.ProjectId };
            booking.GroupAppointmentId = session.Group.Id;
            booking.CustomerId = customer.Id;
            booking.CustomerName = candidate.Name;
            booking.CustomerPhone = candidate.Phone;
            booking.IsAttended = false;
            booking.CreatedAt = DateTime.UtcNow;
            return booking;
        }

        private async Task BroadcastBookingAsync(AutoBookingSession session, BookingCandidate candidate, GroupAppointmentBooking booking)
        {
            try
            {
                await session.Request.HubContext.Clients.Group($"project_{session.Request.ProjectId}").SendAsync("GroupBookingUpdated", new
                {
                    groupId = session.Group.Id,
                    groupName = session.Group.Name,
                    customerPhone = candidate.Phone,
                    customerName = candidate.Name,
                    newBookedCount = session.BookedCount,
                    capacity = session.Group.Capacity,
                    isFull = session.BookedCount >= session.Group.Capacity,
                    bookingId = booking.Id,
                    isAttended = booking.IsAttended,
                    isPaid = booking.IsPaid
                });
            }
            catch (Exception signalRException)
            {
                _logger.LogWarning(signalRException, "SignalR broadcast failed after AI group booking {BookingId}.", booking.Id);
            }
        }

        private static string? NormalizeBookingPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone) || phone.EndsWith("@lid", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return EgyptianPhoneNumber.Extract(phone);
        }

        private static void ApplyFollowUpPolicy(AIBehaviorSettings settings, MarketingAnalysisResult analysisResult)
        {
            var followUp = analysisResult.SuggestedFollowUp;
            if (followUp == null)
            {
                return;
            }

            var policy = settings.FollowUps;
            var isDisabled =
                (string.Equals(followUp.Type, "AppointmentReminder", StringComparison.OrdinalIgnoreCase) && !policy.AppointmentRemindersEnabled) ||
                (string.Equals(followUp.Type, "Nurturing", StringComparison.OrdinalIgnoreCase) && !policy.NurturingEnabled);
            if (!isDisabled) return;

            followUp.Needed = false;
            followUp.AppointmentTime = null;
            followUp.DueDate = null;
            followUp.Notes = null;
        }

        private static void EnforceRequesterBookingPhoneRequirement(
            string channel,
            Customer? customer,
            MarketingAnalysisResult analysisResult)
        {
            var booksRequester = analysisResult.SuggestedGroupBookingPeople.Length == 0 ||
                                 analysisResult.SuggestedGroupBookingPeople.Any(person => person.IsRequester);
            if (string.IsNullOrWhiteSpace(analysisResult.SuggestedGroupBookingId) ||
                !booksRequester ||
                NormalizeBookingPhone(customer?.PhoneNumber) != null)
            {
                return;
            }

            analysisResult.SuggestedGroupBookingId = null;
            analysisResult.ReplyContent = string.Equals(channel, "Messenger", StringComparison.OrdinalIgnoreCase)
                ? "علشان أتمم الحجز، ابعتلي رقم موبايلك الأول لو سمحت."
                : "علشان أتمم الحجز، ابعتلي رقم موبايلك الأول لو سمحت لأن رقمك مش ظاهر عندي.";
            if (analysisResult.SuggestedFollowUp?.Type == "AppointmentReminder")
            {
                analysisResult.SuggestedFollowUp.Needed = false;
            }
        }

        private static void EnforceDistinctBookingPhones(Customer? requester, MarketingAnalysisResult analysisResult)
        {
            var requesterPhone = NormalizeBookingPhone(requester?.PhoneNumber);
            if (requesterPhone == null || string.IsNullOrWhiteSpace(analysisResult.SuggestedGroupBookingId)) return;

            var reusesRequesterPhone = analysisResult.SuggestedGroupBookingPeople.Any(person =>
                !person.IsRequester && NormalizeBookingPhone(person.PhoneNumber) == requesterPhone);
            if (!reusesRequesterPhone) return;

            analysisResult.SuggestedGroupBookingId = null;
            analysisResult.SuggestedGroupBookingPeople = Array.Empty<SuggestedGroupBookingPerson>();
            analysisResult.ReplyContent = "مينفعش نسجل شخص تاني على رقم حضرتك. ابعتلي رقم الموبايل الخاص بالشخص اللي عايز تحجزله لو سمحت.";
            if (analysisResult.SuggestedFollowUp?.Type == "AppointmentReminder")
            {
                analysisResult.SuggestedFollowUp.Needed = false;
            }
        }

        private async Task CompletePendingFollowUpsAsync(AppDbContext dbContext, Guid customerId)
        {
            try
            {
                var pending = await dbContext.FollowUps
                    .IgnoreQueryFilters()
                    .Where(f => f.CustomerId == customerId && f.Status == "Pending")
                    .ToListAsync();

                foreach (var fu in pending)
                {
                    dbContext.FollowUps.Remove(fu);
                }
                await dbContext.SaveChangesAsync();
                Console.WriteLine($"[AIReplyWorker] Deleted {pending.Count} pending follow-ups for skipped customer {customerId}.");
            }
            catch (Exception ex) when (ex is not System.Data.Common.DbException && !ex.ToString().Contains("EntityFrameworkCore"))
            {
                _logger.LogWarning(ex, "Error completing/deleting follow-ups");
            }
        }

        private static async Task<bool> HasReachedDailyAiReplyLimitAsync(AppDbContext dbContext, Modules.Projects.Domain.ProjectSettings settings, string channel)
        {
            if (settings.MaxDailyMessages <= 0)
            {
                return false;
            }

            var projectZone = TimezoneHelper.GetTimeZone(settings.Timezone);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone);
            var localStart = localNow.Date;
            var utcStart = TimeZoneInfo.ConvertTimeToUtc(localStart, projectZone);
            var utcEnd = TimeZoneInfo.ConvertTimeToUtc(localStart.AddDays(1), projectZone);

            var sentToday = await dbContext.Messages
                .IgnoreQueryFilters()
                .Join(
                    dbContext.Conversations.IgnoreQueryFilters(),
                    message => message.ConversationId,
                    conversation => conversation.Id,
                    (message, conversation) => new { message, conversation })
                .CountAsync(row =>
                    row.conversation.ProjectId == settings.ProjectId &&
                    row.conversation.Channel == channel &&
                    row.message.Direction == "Outgoing" &&
                    row.message.MessageType == "Text" &&
                    row.message.Timestamp >= utcStart &&
                    row.message.Timestamp < utcEnd);

            return sentToday >= settings.MaxDailyMessages;
        }

        private async Task<bool> SendWhatsAppTransitionMessageAsync(
            string gatewayUrl,
            Guid projectId,
            string toPhone,
            string customerName,
            string agentName,
            string waMessage)
        {
            var payload = new
            {
                projectId = projectId,
                to = toPhone,
                message = waMessage
            };

            using var httpClient = new HttpClient();
            var jsonPayload = JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            var response = await httpClient.PostAsync($"{gatewayUrl}/api/whatsapp/send", content);
            return response.IsSuccessStatusCode;
        }

        private static async Task SaveCustomerAndBookingPhoneAsync(
            AppDbContext dbContext,
            Guid projectId,
            Customer customer,
            string phoneNumber)
        {
            customer.PhoneNumber = phoneNumber;
            dbContext.Entry(customer).State = EntityState.Modified;

            var bookings = await dbContext.GroupAppointmentBookings
                .Where(booking => booking.ProjectId == projectId && booking.CustomerId == customer.Id)
                .ToListAsync();
            foreach (var booking in bookings)
            {
                booking.CustomerPhone = phoneNumber;
            }

            await dbContext.SaveChangesAsync();
        }

        private async Task HandleMessengerToWhatsAppTransitionAsync(
            AppDbContext dbContext,
            Microsoft.Extensions.Configuration.IConfiguration configuration,
            Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub> hubContext,
            IServiceProvider serviceProvider,
            Customer customer,
            string extractedPhone,
            Modules.Projects.Domain.ProjectSettings settings,
            string pageId,
            string senderPSID)
        {
            var gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            
            var project = await dbContext.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == settings.ProjectId);
            var projectName = project?.Name ?? "المشروع";

            var connectedPage = await dbContext.ConnectedPages
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(cp => cp.FacebookPageId == pageId && cp.IsActive);

            var facebookGraphService = serviceProvider.GetRequiredService<Modules.Facebook.Services.IFacebookGraphService>();
            var behaviorService = serviceProvider.GetRequiredService<Modules.AI.Services.IAIBehaviorSettingsService>();
            var messengerBehavior = behaviorService.Resolve(settings, "Messenger");
            var whatsAppBehavior = behaviorService.Resolve(settings, "WhatsApp");

            if (connectedPage == null)
            {
                Console.WriteLine($"[AIReplyWorker] ConnectedPage not found for pageId: {pageId}");
                return;
            }

            bool waSent = false;
            try
            {
                var agentName = behaviorService.GetAgentName(whatsAppBehavior);
                var transitionMessage = behaviorService.RenderTemplate(whatsAppBehavior.Fallbacks.WhatsAppTransitionMessage, new Modules.AI.Services.AIBehaviorTemplateContext
                {
                    CustomerName = string.IsNullOrWhiteSpace(customer.Name) ? "يا فندم" : customer.Name,
                    AgentName = agentName,
                    ProjectName = projectName,
                    PhoneNumber = extractedPhone,
                    Channel = "WhatsApp"
                });
                waSent = await SendWhatsAppTransitionMessageAsync(
                    gatewayUrl,
                    settings.ProjectId,
                    extractedPhone,
                    customer.Name,
                    agentName,
                    transitionMessage);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[AIReplyWorker] Failed sending WhatsApp message to {extractedPhone}: {ex.Message}");
            }

            if (waSent)
            {
                Console.WriteLine($"[AIReplyWorker] WhatsApp message successfully sent to {extractedPhone}. Proceeding with transition.");

                await SaveCustomerAndBookingPhoneAsync(
                    dbContext,
                    settings.ProjectId,
                    customer,
                    extractedPhone);

                var successMsg = behaviorService.RenderTemplate(messengerBehavior.Fallbacks.WhatsAppTransitionSuccess, new Modules.AI.Services.AIBehaviorTemplateContext
                {
                    CustomerName = customer.Name ?? "يا فندم",
                    AgentName = behaviorService.GetAgentName(messengerBehavior),
                    ProjectName = projectName,
                    PhoneNumber = extractedPhone,
                    Channel = "Messenger"
                });
                await facebookGraphService.SendMessageAsync(connectedPage.FacebookPageId, connectedPage.PageAccessToken, senderPSID, successMsg);

                var messengerConvo = await dbContext.Conversations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ProjectId == settings.ProjectId && c.CustomerId == customer.Id && c.Channel == "Messenger" && c.Status != "Closed");

                if (messengerConvo != null)
                {
                    var msg = new Message
                    {
                        ConversationId = messengerConvo.Id,
                        ExternalMessageId = $"msg_fb_fu_{Guid.NewGuid():N}",
                        Direction = "Outgoing",
                        Content = successMsg,
                        MessageType = "Text",
                        Timestamp = DateTime.UtcNow
                    };
                    dbContext.Messages.Add(msg);
                    await dbContext.SaveChangesAsync();

                    await hubContext.Clients.Group($"project_{settings.ProjectId}").SendAsync("ReceiveMessage", new
                    {
                        id = msg.Id,
                        conversationId = messengerConvo.Id,
                        senderType = "AI",
                        content = successMsg,
                        createdAt = msg.Timestamp,
                        status = "Sent",
                        channel = "Messenger"
                    });
                }

                var pendingMessengerFollowUps = await dbContext.FollowUps
                    .IgnoreQueryFilters()
                    .Where(f => f.CustomerId == customer.Id && f.Status == "Pending")
                    .ToListAsync();

                foreach (var fu in pendingMessengerFollowUps)
                {
                    fu.Status = "Cancelled";
                    dbContext.Entry(fu).State = EntityState.Modified;
                }
                await dbContext.SaveChangesAsync();
                Console.WriteLine($"[AIReplyWorker] Cancelled {pendingMessengerFollowUps.Count} pending follow-ups for customer {customer.Id} due to WhatsApp transition.");

                var newFollowUp = new FollowUp
                {
                    Id = Guid.NewGuid(),
                    ProjectId = settings.ProjectId,
                    CustomerId = customer.Id,
                    Type = "Nurturing",
                    DueDate = DateTime.UtcNow.AddHours(24),
                    Notes = behaviorService.RenderTemplate(whatsAppBehavior.Fallbacks.FollowUpDefault, new Modules.AI.Services.AIBehaviorTemplateContext
                    {
                        CustomerName = customer.Name ?? "يا فندم",
                        AgentName = behaviorService.GetAgentName(whatsAppBehavior),
                        ProjectName = projectName,
                        PhoneNumber = extractedPhone,
                        Channel = "WhatsApp"
                    }),
                    Status = "Pending"
                };
                dbContext.FollowUps.Add(newFollowUp);
                await dbContext.SaveChangesAsync();
                Console.WriteLine($"[AIReplyWorker] Scheduled new WhatsApp follow-up {newFollowUp.Id} for transitioned customer {customer.Id}.");
            }
            else
            {
                Console.WriteLine($"[AIReplyWorker] WhatsApp message delivery failed for {extractedPhone}. Falling back to Messenger.");

                var failureMsg = behaviorService.RenderTemplate(messengerBehavior.Fallbacks.WhatsAppTransitionFailure, new Modules.AI.Services.AIBehaviorTemplateContext
                {
                    CustomerName = customer.Name ?? "يا فندم",
                    AgentName = behaviorService.GetAgentName(messengerBehavior),
                    ProjectName = projectName,
                    PhoneNumber = extractedPhone,
                    Channel = "Messenger"
                });
                await facebookGraphService.SendMessageAsync(connectedPage.FacebookPageId, connectedPage.PageAccessToken, senderPSID, failureMsg);

                var messengerConvo = await dbContext.Conversations
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.ProjectId == settings.ProjectId && c.CustomerId == customer.Id && c.Channel == "Messenger" && c.Status != "Closed");

                if (messengerConvo != null)
                {
                    var msg = new Message
                    {
                        ConversationId = messengerConvo.Id,
                        ExternalMessageId = $"msg_fb_fu_err_{Guid.NewGuid():N}",
                        Direction = "Outgoing",
                        Content = failureMsg,
                        MessageType = "Text",
                        Timestamp = DateTime.UtcNow
                    };
                    dbContext.Messages.Add(msg);
                    await dbContext.SaveChangesAsync();

                    await hubContext.Clients.Group($"project_{settings.ProjectId}").SendAsync("ReceiveMessage", new
                    {
                        id = msg.Id,
                        conversationId = messengerConvo.Id,
                        senderType = "AI",
                        content = failureMsg,
                        createdAt = msg.Timestamp,
                        status = "Sent",
                        channel = "Messenger"
                    });
                }
            }
            
            var activeConvo = await dbContext.Conversations
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ProjectId == settings.ProjectId && c.CustomerId == customer.Id && c.Channel == "Messenger" && c.Status != "Closed");
            if (activeConvo != null)
            {
                try
                {
                    var redis = serviceProvider.GetRequiredService<StackExchange.Redis.IConnectionMultiplexer>().GetDatabase();
                    await redis.KeyDeleteAsync($"ai_typing:{activeConvo.Id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[AIReplyWorker] Typing cleanup Redis failed: {ex.Message}");
                }
                await hubContext.Clients.Group($"project_{settings.ProjectId}").SendAsync("AITyping", new
                {
                    conversationId = activeConvo.Id,
                    isTyping = false
                });
            }
        }
    }
}
