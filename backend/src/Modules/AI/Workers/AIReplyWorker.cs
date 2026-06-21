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
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Modules.Conversations.Domain;
using Modules.GroupAppointments.Domain;

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
            var channel = @event.Channel ?? "WhatsApp";

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

            // Decide which API key to use. Per-project key, or fall back to system default.
            string apiKeyOverride = !string.IsNullOrEmpty(settings.GeminiApiKey) ? settings.GeminiApiKey : null;

            string brainContext = null;
            string cachedContentId = null;

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
                var tonePref = !string.IsNullOrEmpty(settings.AiTonePreference) ? settings.AiTonePreference : "العامية المصرية الروشة والصايعة";
                var targetAud = !string.IsNullOrEmpty(settings.AiTargetAudience) ? settings.AiTargetAudience : "طلاب كورس كول سنتر يبحثون عن عمل";
                var agentName = _aiMarketingBrain.GetCurrentAgentName();
                var staticPrompt = _aiMarketingBrain.BuildStaticPrompt(agentName, tonePref, targetAud, approvedChunksText);

                var geminiClient = scope.ServiceProvider.GetRequiredService<Modules.AI.Services.IGeminiClient>();
                int staticTokensCount = await geminiClient.CountTokensAsync(staticPrompt, apiKeyOverride, settings.GeminiModel);
                Console.WriteLine($"[AIReplyWorker] Project {@event.ProjectId} static prompt token count: {staticTokensCount}");

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

                    foreach (var g in availableGroups)
                    {
                        // Convert the database UTC datetime back to the project's local timezone
                        var utcTime = DateTime.SpecifyKind(g.DateTime, DateTimeKind.Utc);
                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, projectZone);
                        var modeText = g.Mode == "online" ? "أونلاين (Online)" : "في السنتر (Offline)";
                        var daysText = GetArabicDaysText(g.Days);
                        var daysLine = string.IsNullOrEmpty(daysText) ? "" : $"\n  أيام الموعد: {daysText}";
                        var dateText = $"{GetArabicDayName(localTime.DayOfWeek)} {localTime:d/M}";
                        groupsContextList.Add($"- معرف المجموعة (ID): {g.Id}\n  نوع المجموعة: {modeText}{daysLine}\n  تاريخ الموعد: {dateText}\n  الموعد: الساعة {localTime:h:mm} {(localTime.Hour >= 12 ? "مساءً" : "صباحاً")}\n  عدد المشتركين المسجلين حالياً: {g.Bookings.Count} من أصل {g.Capacity}");
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
                        fullGroupsContextList.Add($"- معرف المجموعة (ID): {g.Id}\n  نوع المجموعة: {modeText}{daysLine}\n  تاريخ الموعد: {dateText}\n  الموعد: الساعة {localTime:h:mm} {(localTime.Hour >= 12 ? "مساءً" : "صباحاً")} (مكتملة العدد تماماً - ممتلئة)\n  عدد المشتركين المسجلين حالياً: {g.Bookings.Count} من أصل {g.Capacity}");
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

                        bookedGroupInfo = $"Group Name: {bookedGroup.Name}\nGroup ID: {bookedGroup.Id}\nSchedule: {modeText} ({bookedDateText} at {timeText})";

                        alreadyBookedNote = $"\nملاحظة هامة جداً وصارمة: العميل مسجل حالياً ومحجوز في الموعد التالي: {scheduleInfo} (اسم المجموعة: {bookedGroup.Name}، معرف المجموعة ID: {bookedGroup.Id})." +
                                            $"\n- إذا سأل العميل عن موعده أو مجموعته أو متى تم حجزه، أخبره بدقة وصراحة تامة بالموعد الحالي المحجوز فيه: {scheduleInfo} (ولا تخمن أو تخترع أي موعد آخر من المجموعات المتاحة!)." +
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

                    var groupsContextText = "معلومات مواعيد المجموعات المتاحة للحجز (Group Appointments):\n" +
                                            "إذا سأل العميل عن المجموعات أو المواعيد المتاحة أو يرغب في الحجز، اعرض عليه المجموعات المتاحة المناسبة له مع توضيح نوع كل مجموعة (سواء كانت أونلاين أو في السنتر)، والأيام النشطة للمجموعة، وموعدها (الساعة والتوقيت) فقط. لا تذكر أبداً عدد الأماكن المتبقية أو السعة أو أي أرقام. إذا أراد الحجز في مجموعة محددة،ضع suggestedGroupBookingId = معرف المجموعة (ID) وأكد له الحجز في ردك. النظام سيسجله تلقائياً. لا ترسل أي رابط حجز للعميل. إذا لم تكن هناك مجموعات متاحة، أخبره أن المجموعات مكتملة حالياً.\n" +
                                            "تنبيه هام جداً وصارم بشأن توافر المجموعات وسعتها: إذا كانت المجموعة مدرجة في 'قائمة المجموعات المتاحة حالياً' بالأسفل، فهذا يعني بشكل قاطع وبقوة النظام أنها متاحة وبها أماكن شاغرة ومفتوحة للحجز الفعلي والمباشر. تجاهل تماماً أي معلومات قديمة أو متعارضة في القاعدة المعرفية أو الملفات المرفقة (مثل التي تدعي أن مجموعات السنتر/سيدي جابر مكتملة تماماً، أو تدعي أن المجموعات الأونلاين مكتملة، أو تطلب تسجيل العملاء في 'قائمة الانتظار'، أو تحدد سعة معينة للمجموعات الأونلاين أو الأوفلاين مثل 12 إلى 20 أو 21). اعتمد فقط وحصرياً على أرقام المشتركين والسعة الموضحة في القائمة بالأسفل (مثلاً إذا كان عدد المشتركين الحالي 41 من أصل 60، فهذا يعني أن هناك 19 مكاناً شاغراً، وبالتالي المجموعة ليست كاملة بل مفتوحة للحجز الفوري). يُمنع منعاً باتاً ذكر 'قائمة الانتظار' للعميل أو الادعاء بأن المجموعات مكتملة طالما أن المجموعة تظهر في القائمة بالأسفل؛ بل احجز للعميل فيها مباشرة وبشكل طبيعي إذا رغب في ذلك، مع الالتزام التام بعدم ذكر العدد أو السعة أو أي أرقام أو إحصائيات للحجز للعميل إطلاقاً (مثل لا تقل له 'متبقي 19 مكان' أو 'العدد الحالي 41/60'، بل قل له فقط 'سجلتك في المجموعة' أو 'المجموعة متاحة للحجز').\n" +
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
                var gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
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
                settings.AiTonePreference,
                settings.AiTargetAudience,
                settings.GeminiModel,
                cachedContentId);

            await ApplyKnowledgePricingGuardAsync(dbContext, @event.ProjectId, @event.Content, analysisResult);

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
                FollowUpNotes = analysisResult.SuggestedFollowUp?.Notes
            };
            await _eventBus.PublishAsync(crmSuggestion);
            Console.WriteLine($"[AIReplyWorker] Published CRMUpdateSuggestedEvent for {@event.Sender}");

            // 2. Publish AI Reply
            var replyGeneratedEvent = new AIReplyGeneratedEvent
            {
                ProjectId = @event.ProjectId,
                Sender = @event.Sender,
                Content = analysisResult.ReplyContent,
                Buttons = analysisResult.SuggestedButtons ?? Array.Empty<string>(),
                Channel = @event.Channel ?? "WhatsApp",
                ChannelMetadata = @event.ChannelMetadata,
                Reaction = analysisResult.SuggestedReaction
            };

            await _eventBus.PublishAsync(replyGeneratedEvent);
            Console.WriteLine($"[AIReplyWorker] Published AIReplyGeneratedEvent for {@event.Sender}");

            // 2.5. Process AI Auto-Booking if suggestedGroupBookingId is set
            if (!string.IsNullOrEmpty(analysisResult.SuggestedGroupBookingId))
            {
                try
                {
                    if (Guid.TryParse(analysisResult.SuggestedGroupBookingId, out var groupId))
                    {
                        var group = await dbContext.GroupAppointments
                            .Include(g => g.Bookings)
                            .FirstOrDefaultAsync(g => g.Id == groupId && g.ProjectId == @event.ProjectId && g.IsActive);

                        if (group == null)
                        {
                            Console.WriteLine($"[AIReplyWorker] Auto-booking failed: Group {groupId} not found or inactive.");
                        }
                        else
                        {
                            // Adjust date if group date has passed
                            var projectZone = TimezoneHelper.GetTimeZone(settings?.Timezone);
                            var localGroupDateTime = TimeZoneInfo.ConvertTimeFromUtc(group.DateTime, projectZone);
                            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone);

                            bool isGroupActive = true;
                            if (localNow > localGroupDateTime)
                            {
                                var timeDiff = localNow - localGroupDateTime;
                                if (timeDiff.TotalHours >= 24)
                                {
                                    if (group.IsActive)
                                    {
                                        group.IsActive = false;
                                        dbContext.Entry(group).State = EntityState.Modified;
                                        await dbContext.SaveChangesAsync();
                                        Console.WriteLine($"[AIReplyWorker] Deactivated expired group {group.Id} because 24 hours have passed.");
                                    }
                                    isGroupActive = false;
                                }
                            }

                            if (!isGroupActive)
                            {
                                Console.WriteLine($"[AIReplyWorker] Auto-booking failed: Group '{group.Name}' has passed completely.");
                            }
                            else if (group.Bookings.Count >= group.Capacity)
                            {
                                Console.WriteLine($"[AIReplyWorker] Auto-booking failed: Group '{group.Name}' is full ({group.Bookings.Count}/{group.Capacity}).");
                            }
                            else
                            {
                                // Resolve or create customer for the booking
                            var bookingCustomerId = customer?.Id ?? Guid.Empty;
                            var bookingCustomerName = customer?.Name ?? @event.Sender;
                            var bookingCustomerPhone = @event.Sender;

                            // Check if already booked in ANY group for this project
                            var existingBooking = await dbContext.GroupAppointmentBookings
                                .FirstOrDefaultAsync(b => b.ProjectId == @event.ProjectId && (b.CustomerPhone == bookingCustomerPhone || b.CustomerId == bookingCustomerId));

                            if (existingBooking != null)
                            {
                                if (existingBooking.GroupAppointmentId == groupId)
                                {
                                    Console.WriteLine($"[AIReplyWorker] Auto-booking skipped: Customer {bookingCustomerPhone} already registered in the SAME group '{group.Name}'.");
                                }
                                else
                                {
                                    // Transfer booking to new group
                                    existingBooking.GroupAppointmentId = groupId;
                                    existingBooking.IsAttended = false; // Reset attendance
                                    // Keep existingBooking.IsPaid as is so their payment status carries over!
                                    existingBooking.CreatedAt = DateTime.UtcNow;

                                    dbContext.Entry(existingBooking).State = EntityState.Modified;
                                    
                                    // Update customer notes to document the transfer
                                    if (customer != null)
                                    {
                                        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone);
                                        customer.Notes = (customer.Notes ?? string.Empty) + $"\nتم نقل حجز الطالب من مجموعة إلى مجموعة: {group.Name} (تلقائياً بالـ AI) بتاريخ {localTime:yyyy-MM-dd HH:mm}";
                                        dbContext.Entry(customer).State = EntityState.Modified;
                                    }

                                    await dbContext.SaveChangesAsync();
                                    Console.WriteLine($"[AIReplyWorker] ✅ Auto-transferred customer {bookingCustomerPhone} ('{bookingCustomerName}') to group '{group.Name}'.");

                                    // Broadcast update via SignalR to refresh dashboard
                                    try
                                    {
                                        var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub>>();
                                        await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("GroupBookingUpdated", new
                                        {
                                            groupId = groupId,
                                            groupName = group.Name,
                                            customerPhone = bookingCustomerPhone,
                                            customerName = bookingCustomerName,
                                            newBookedCount = group.Bookings.Count + 1,
                                            capacity = group.Capacity,
                                            isFull = (group.Bookings.Count + 1) >= group.Capacity,
                                            bookingId = existingBooking.Id,
                                            isAttended = existingBooking.IsAttended,
                                            isPaid = existingBooking.IsPaid
                                        });
                                    }
                                    catch (Exception signalREx)
                                    {
                                        Console.WriteLine($"[AIReplyWorker] SignalR broadcast for group booking transfer failed: {signalREx.Message}");
                                    }
                                }
                            }
                            else
                            {
                                // Create the booking
                                var booking = new GroupAppointmentBooking
                                {
                                    Id = Guid.NewGuid(),
                                    ProjectId = @event.ProjectId,
                                    GroupAppointmentId = groupId,
                                    CustomerId = bookingCustomerId,
                                    CustomerName = bookingCustomerName,
                                    CustomerPhone = bookingCustomerPhone
                                };

                                dbContext.GroupAppointmentBookings.Add(booking);
                                await dbContext.SaveChangesAsync();

                                Console.WriteLine($"[AIReplyWorker] ✅ Auto-booked customer {bookingCustomerPhone} ('{bookingCustomerName}') into group '{group.Name}' ({group.Bookings.Count + 1}/{group.Capacity}).");

                                // Broadcast update via SignalR to refresh dashboard
                                try
                                {
                                    var hubContext = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Modules.Conversations.Hubs.NotificationHub>>();
                                    await hubContext.Clients.Group($"project_{@event.ProjectId}").SendAsync("GroupBookingUpdated", new
                                    {
                                        groupId = groupId,
                                        groupName = group.Name,
                                        customerPhone = bookingCustomerPhone,
                                        customerName = bookingCustomerName,
                                        newBookedCount = group.Bookings.Count + 1,
                                        capacity = group.Capacity,
                                        isFull = (group.Bookings.Count + 1) >= group.Capacity,
                                        bookingId = booking.Id,
                                        isAttended = booking.IsAttended,
                                        isPaid = booking.IsPaid
                                    });
                                }
                                catch (Exception signalREx)
                                {
                                    Console.WriteLine($"[AIReplyWorker] SignalR broadcast for group booking failed: {signalREx.Message}");
                                }
                            }
                        }
                    }
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
                    var bookingCustomerPhone = @event.Sender;

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
            if (channel == "WhatsApp" && !string.IsNullOrEmpty(analysisResult.SuggestedReaction))
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

                            var gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";

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
    }
}
