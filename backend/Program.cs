using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using System;
using Hangfire;
using Hangfire.PostgreSql;
using Serilog;
using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);

var advertisingSection = builder.Configuration.GetSection(Modules.Advertising.Services.AdvertisingOptions.SectionName);
var advertisingOptions = advertisingSection.Get<Modules.Advertising.Services.AdvertisingOptions>() ?? new();
var advertisingStartupErrors = Modules.Advertising.Services.AdvertisingStartupValidator.Validate(
    advertisingOptions,
    builder.Environment.EnvironmentName);
if (advertisingStartupErrors.Count > 0)
{
    throw new InvalidOperationException(string.Join(
        Environment.NewLine,
        advertisingStartupErrors.Select(error => $"{error.Code}: {error.Message}")));
}

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(), "logs/audit.json", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Initialize Firebase Admin SDK
var firebaseKeyPath = builder.Configuration["Firebase:ServiceAccountPath"];
if (!string.IsNullOrEmpty(firebaseKeyPath) && System.IO.File.Exists(firebaseKeyPath))
{
    try
    {
        FirebaseApp.Create(new AppOptions
        {
            Credential = GoogleCredential.FromFile(firebaseKeyPath)
        });
        Console.WriteLine("✅ Firebase Admin SDK initialized successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Failed to initialize Firebase Admin SDK: {ex.Message}");
    }
}
else
{
    Console.WriteLine("⚠️ Firebase:ServiceAccountPath key file not found or empty. Push notifications will be disabled.");
}

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<Modules.TalkTips.Services.TalkTipsTrialStatusClient>(client =>
{
    client.BaseAddress = new Uri("https://api.talktips-academy.com/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data-protection-keys");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
    .SetApplicationName("SmartCustomerCore");

var allowedCorsOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? new[]
    {
        "http://localhost:3000",
        "http://127.0.0.1:3000",
        "http://localhost:3001",
        "http://127.0.0.1:3001",
        "http://localhost:3002",
        "http://127.0.0.1:3002",
        "http://localhost",
        "http://127.0.0.1"
    };

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy
            .SetIsOriginAllowed(origin => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Configure JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var secret = builder.Configuration["JWT:Secret"] ?? "a_very_long_and_secure_secret_key_that_is_at_least_32_characters_long";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret))
    };
});

// Configure EF Core with PostgreSQL
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        o => o.UseVector()));

// Configure Hangfire with PostgreSQL Storage
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => 
        options.UseNpgsqlConnection(builder.Configuration.GetConnectionString("DefaultConnection"))));

builder.Services.AddHangfireServer(options =>
{
    options.SchedulePollingInterval = TimeSpan.FromSeconds(5);
});

// Dependency Injection registrations
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IProjectAuthorizationService, ProjectAuthorizationService>();
builder.Services.AddScoped<IProjectSecretVault, ProjectSecretVault>();

builder.Services.Configure<Modules.Advertising.Services.AdvertisingOptions>(advertisingSection);
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingSecretVault>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingReadinessService>();
builder.Services.AddHttpClient<Modules.Advertising.Services.WhatsAppGatewaySessionClient>(client =>
    client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<Modules.Advertising.Services.AutonomyEnvelopeService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingDisconnectService>();
builder.Services.AddScoped<Modules.Advertising.Services.CampaignPlanCompiler>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingAiWorkCoordinator>();
builder.Services.AddScoped<Modules.Advertising.Workers.ConnectionDisconnectWorker>();
builder.Services.AddScoped<Modules.Advertising.Services.ConversionIngressService>();
builder.Services.AddScoped<Modules.Advertising.Services.ConversionLedgerService>();
builder.Services.AddScoped<Modules.Advertising.Services.FacebookAdsOAuthService>();
builder.Services.AddScoped<Modules.Advertising.Services.ExistingCampaignImportService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingSafetyEngine>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingDecisionAi>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingDecisionService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingDecisionImpactService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingOwnershipPolicy>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingEmergencyStopService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingDisableService>();
builder.Services.AddScoped<Modules.Advertising.Services.WhatsAppCreativeTestService>();
builder.Services.AddScoped<Modules.Advertising.Services.AllocationPolicyService>();
builder.Services.AddSingleton<Modules.Advertising.Services.AdvertisingEvidenceService>();
builder.Services.AddScoped<Modules.Advertising.Workers.AdvertisingCommandWorker>();
builder.Services.AddScoped<Modules.Advertising.Jobs.AdvertisingRecurringJobs>();
builder.Services.AddScoped<Modules.Advertising.Jobs.AdvertisingRetentionJob>();
builder.Services.AddScoped<Modules.Advertising.Workers.KnowledgeProjectionConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.MediaProjectionConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.BusinessOutcomeConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.GatewayLeadObservationConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.ProjectContextProjectionConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.ProjectAiConfigurationProjectionConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.OfferAvailabilityProjectionConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.ConsentProjectionConsumer>();
builder.Services.AddScoped<Modules.WhatsApp.Workers.WhatsAppInboundRouteConsumer>();
builder.Services.AddScoped<Modules.AI.Workers.AdvertisingAiWorkConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.AdvertisingAiWorkResultConsumer>();
builder.Services.AddScoped<Modules.Advertising.Jobs.AdvertisingProjectionBackfillJob>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingAuditService>();
builder.Services.AddScoped<Modules.Advertising.Infrastructure.Facebook.MetaCreativeSourceClient>();
builder.Services.AddScoped<Modules.Advertising.Services.MetaProviderReconciliationService>();
builder.Services.AddScoped<Modules.Advertising.Services.CampaignProvisioningService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingCampaignBootstrapService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingCloneService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingReportingWindowService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingExperimentService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingReferralProtector>();
builder.Services.AddScoped<Shared.Security.IAdvertisingReferralProtector>(sp => sp.GetRequiredService<Modules.Advertising.Services.AdvertisingReferralProtector>());
builder.Services.AddScoped<Modules.Conversations.Services.WhatsAppInboundEventPublisher>();
builder.Services.AddScoped<Modules.Conversations.Workers.WhatsAppInboundMessageConsumer>();
builder.Services.AddScoped<Modules.Advertising.Workers.WhatsAppAttributionObservationConsumer>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingAttributionService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingWebhookSourceService>();
builder.Services.AddScoped<Modules.Advertising.Services.AdvertisingTrackingHealthService>();
builder.Services.AddScoped<Modules.Advertising.Jobs.ConversionDeliveryJob>();
builder.Services.AddScoped<Shared.Audit.ElasticsearchAuditIndexer>();
builder.Services.AddScoped<Shared.Queue.IntegrationOutboxDispatcher>();
builder.Services.AddSingleton<Shared.Queue.IntegrationEventTypeRegistry>();
var metaClient = builder.Services.AddHttpClient<Modules.Advertising.Infrastructure.Facebook.MetaAdsClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var version = config["Advertising:Meta:GraphApiVersion"] ?? config["FACEBOOK_GRAPH_API_VERSION"] ?? "v26.0";
    client.BaseAddress = new Uri($"https://graph.facebook.com/{version.Trim('/')}/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
var permitsAdvertisingMock = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test");
if (advertisingOptions.Enabled && permitsAdvertisingMock && advertisingOptions.Meta.UseMock)
    metaClient.ConfigurePrimaryHttpMessageHandler(() => new Modules.Advertising.Infrastructure.Facebook.FakeMetaAdsHandler());
var metaGraphClient = builder.Services.AddHttpClient<Modules.Advertising.Infrastructure.Facebook.MetaGraphClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var version = config["Advertising:Meta:GraphApiVersion"] ?? config["FACEBOOK_GRAPH_API_VERSION"] ?? "v26.0";
    client.BaseAddress = new Uri($"https://graph.facebook.com/{version.Trim('/')}/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
if (advertisingOptions.Enabled && permitsAdvertisingMock && advertisingOptions.Meta.UseMock)
    metaGraphClient.ConfigurePrimaryHttpMessageHandler(() => new Modules.Advertising.Infrastructure.Facebook.FakeMetaAdsHandler());
builder.Services.AddScoped<Modules.Advertising.Infrastructure.Facebook.MetaCapabilityClient>();
var metaInsightsClient = builder.Services.AddHttpClient<Modules.Advertising.Infrastructure.Facebook.MetaInsightsClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var version = config["Advertising:Meta:GraphApiVersion"] ?? config["FACEBOOK_GRAPH_API_VERSION"] ?? "v26.0";
    client.BaseAddress = new Uri($"https://graph.facebook.com/{version.Trim('/')}/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
if (advertisingOptions.Enabled && permitsAdvertisingMock && advertisingOptions.Meta.UseMock)
    metaInsightsClient.ConfigurePrimaryHttpMessageHandler(() => new Modules.Advertising.Infrastructure.Facebook.FakeMetaAdsHandler());
var metaBusinessMessagingClient = builder.Services.AddHttpClient<Modules.Advertising.Infrastructure.Facebook.MetaBusinessMessagingClient>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var version = config["Advertising:Meta:GraphApiVersion"] ?? config["FACEBOOK_GRAPH_API_VERSION"] ?? "v26.0";
    client.BaseAddress = new Uri($"https://graph.facebook.com/{version.Trim('/')}/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
if (advertisingOptions.Enabled && permitsAdvertisingMock && advertisingOptions.Meta.UseMock)
    metaBusinessMessagingClient.ConfigurePrimaryHttpMessageHandler(() => new Modules.Advertising.Infrastructure.Facebook.FakeMetaAdsHandler());
builder.Services.AddSingleton<Modules.Advertising.Services.BudgetAllocator>();

// Register Redis Connection Multiplexer
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connectionString = config["Redis:ConnectionString"] ?? "localhost:6379";
    return StackExchange.Redis.ConnectionMultiplexer.Connect(connectionString);
});

// Register Elasticsearch Client
builder.Services.AddSingleton<Elastic.Clients.Elasticsearch.ElasticsearchClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var elasticUrl = config["Elasticsearch:Url"] ?? "http://localhost:9200";
    var settings = new Elastic.Clients.Elasticsearch.ElasticsearchClientSettings(new Uri(elasticUrl))
        .DefaultIndex("smart_whatsapp_messages");
    return new Elastic.Clients.Elasticsearch.ElasticsearchClient(settings);
});

// Register Conversations Aggregator
builder.Services.AddScoped<Modules.Conversations.Services.IMessageAggregator, Modules.Conversations.Services.MessageAggregator>();
builder.Services.AddScoped<Modules.Conversations.Services.ConversationReplyWindowService>();
builder.Services.AddScoped<Modules.Conversations.Services.IAssignmentEngine, Modules.Conversations.Services.AssignmentEngine>();
builder.Services.AddScoped<Modules.Conversations.Services.CustomerOptOutService>();
builder.Services.AddScoped<Modules.CRM.Services.AutomationFollowUpService>();
builder.Services.AddScoped<Modules.Conversations.Jobs.UnansweredConversationRecoveryDependencies>();
builder.Services.AddScoped<Modules.Conversations.Jobs.UnansweredConversationRecoveryJob>();
builder.Services.AddScoped<Modules.Conversations.Jobs.ConversationReplyWindowDispatcher>();
builder.Services.AddScoped<Modules.Conversations.Jobs.WhatsAppLidContactRecoveryJob>();

// Register Media Services
builder.Services.AddSingleton<Modules.Media.Services.IMinIoStorageService, Modules.Media.Services.MinIoStorageService>();
builder.Services.AddSingleton<Shared.Storage.IObjectStorage>(sp =>
    (Shared.Storage.IObjectStorage)sp.GetRequiredService<Modules.Media.Services.IMinIoStorageService>());
builder.Services.AddScoped<Modules.Media.Services.IAssetService, Modules.Media.Services.AssetService>();
builder.Services.AddScoped<Modules.Media.Services.IImageTransformer, Modules.Media.Services.ImageTransformer>();
builder.Services.AddScoped<Modules.Media.Jobs.IMediaWorker, Modules.Media.Jobs.MediaWorker>();

// Register Audit Services
builder.Services.AddScoped<Modules.Audit.Services.IAuditService, Modules.Audit.Services.AuditService>();

// Register System Health Services
builder.Services.AddScoped<Modules.SystemHealth.Services.ISystemHealthService, Modules.SystemHealth.Services.SystemHealthService>();

// Register Gemini Mock Handler
builder.Services.AddSingleton<Modules.AI.Services.IGeminiMockHandler, Modules.AI.Services.GeminiMockHandler>();

// Register Gemini Client
builder.Services.AddSingleton<Modules.AI.Services.IGeminiClient, Modules.AI.Services.GeminiClient>();

// Keep customer chat generation isolated from Gemini-backed content and automation.
builder.Services.AddHttpClient<Modules.AI.Services.OpenAiResponsesClient>(client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/");
    client.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<Modules.AI.Services.XaiResponsesClient>(client =>
{
    client.BaseAddress = new Uri("https://api.x.ai/");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// Register AI Marketing Brain
builder.Services.AddScoped<Modules.AI.Services.IAIMarketingBrain, Modules.AI.Services.AIMarketingBrain>();
builder.Services.AddScoped<Modules.AI.Services.IAIBehaviorSettingsService, Modules.AI.Services.AIBehaviorSettingsService>();
builder.Services.AddScoped<Modules.GroupAppointments.Services.GroupBookingCoordinator>();
builder.Services.AddScoped<Modules.AI.Services.AiGroupBookingOrchestrator>();

// Register AI Company Brain
builder.Services.AddScoped<Modules.Brain.Services.IAICompanyBrain, Modules.Brain.Services.AICompanyBrain>();
builder.Services.AddScoped<Modules.Brain.Services.IKnowledgeBaseService, Modules.Brain.Services.KnowledgeBaseService>();

builder.Services.AddScoped<Modules.Content.Services.LogoBrandingService>();
builder.Services.AddSingleton<Modules.Content.Services.ContentImagePreviewService>();
builder.Services.AddScoped<Modules.Content.Services.ContentGenerationService>();
builder.Services.AddScoped<Modules.Content.Services.ContentPublishingService>();
builder.Services.AddScoped<Modules.Content.Services.ContentWeeklyPlanService>();
builder.Services.AddScoped<Modules.Content.Jobs.ContentAutomationJob>();
builder.Services.AddScoped<Modules.Content.Services.ContentVideoReadinessService>();
builder.Services.AddScoped<Modules.Content.Services.ContentVideoPlanningService>();
builder.Services.AddScoped<Modules.Content.Services.ContentVideoMediaService>();
builder.Services.AddScoped<Modules.Content.Jobs.ContentVideoDispatchService>();
builder.Services.AddScoped<Modules.Content.Jobs.ContentVideoJob>();
builder.Services.AddHttpClient<Modules.Content.Services.GeminiImageClient>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient<Modules.Content.Services.GeminiOmniVideoClient>(client =>
{
    client.BaseAddress = new Uri("https://aiplatform.googleapis.com/");
    client.Timeout = TimeSpan.FromMinutes(5);
});
builder.Services.AddHttpClient<Modules.Content.Services.FacebookPhotoPublisher>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var version = config["Advertising:Meta:GraphApiVersion"] ?? config["FACEBOOK_GRAPH_API_VERSION"] ?? "v26.0";
    client.BaseAddress = new Uri($"https://graph.facebook.com/{version.Trim('/')}/");
    client.Timeout = TimeSpan.FromMinutes(2);
});

// Register Risk Analyzer for Approvals
builder.Services.AddScoped<Modules.Approvals.Services.IRiskAnalyzer, Modules.Approvals.Services.RiskAnalyzer>();

// Register Campaigns Services
builder.Services.AddScoped<Modules.Campaigns.Application.Services.ICampaignAIService, Modules.Campaigns.Application.Services.CampaignAIService>();
builder.Services.AddScoped<Modules.Campaigns.Jobs.CampaignSenderJob>();

// Register Analytics Services
builder.Services.AddScoped<Modules.Analytics.Application.Services.IAnalyticsEngine, Modules.Analytics.Application.Services.AnalyticsEngine>();
builder.Services.AddScoped<Modules.Analytics.Jobs.DailyAnalyticsJob>();
builder.Services.AddScoped<Modules.Analytics.Application.Services.ConversationSalesAnalyzer>();
builder.Services.AddScoped<Modules.Analytics.Application.Services.SalesIntelligenceService>();
builder.Services.AddScoped<Modules.Analytics.Jobs.SalesIntelligenceJob>();

// Register Integrations Services
builder.Services.AddScoped<Modules.Integrations.Services.IProjectIntegrationService, Modules.Integrations.Services.ProjectIntegrationService>();

// Register Search Services & Workers
builder.Services.AddScoped<Modules.Search.Application.Services.ISearchService, Modules.Search.Application.Services.SearchService>();
builder.Services.AddScoped<Modules.Search.Workers.ElasticsearchIndexerWorker>();

// Register Customer Memory Services & Workers
builder.Services.AddScoped<Modules.Customers.Services.ICustomerMemoryService, Modules.Customers.Services.CustomerMemoryService>();
builder.Services.AddScoped<Modules.Customers.Workers.CustomerMemoryWorker>();

// Register Human Messaging Engine
builder.Services.AddSingleton<Modules.WhatsApp.Services.IHumanMessagingEngine, Modules.WhatsApp.Services.HumanMessagingEngine>();
builder.Services.AddScoped<Modules.WhatsApp.Services.WhatsAppAccountService>();
builder.Services.AddScoped<Modules.WhatsApp.Services.WhatsAppCustomerMergeService>();
builder.Services.AddScoped<Modules.WhatsApp.Services.WhatsAppConversationService>();

// Register Event Handlers
builder.Services.AddScoped<Modules.AI.Workers.AIReplyWorker>();
builder.Services.AddScoped<Modules.WhatsApp.Workers.ReplySender>();
builder.Services.AddScoped<Modules.CRM.Services.ICRMAutoUpdateEngine, Modules.CRM.Services.CRMAutoUpdateEngine>();
builder.Services.AddScoped<Modules.CRM.Workers.CRMWorker>();
builder.Services.AddScoped<Modules.Workflows.Services.IWorkflowEngine, Modules.Workflows.Services.WorkflowEngine>();
builder.Services.AddScoped<Modules.Workflows.Workers.WorkflowWorker>();

// Register Facebook Module Services
builder.Services.AddScoped<Modules.Facebook.Services.IFacebookGraphService, Modules.Facebook.Services.FacebookGraphService>();
builder.Services.AddScoped<Modules.Facebook.Services.IFacebookOAuthService, Modules.Facebook.Services.FacebookOAuthService>();
builder.Services.AddScoped<Modules.Facebook.Workers.FacebookReplySender>();

// Register CRM/Follow-up Hosted Services
builder.Services.AddHostedService<Modules.CRM.Services.FollowUpScheduler>();
builder.Services.AddSingleton<Modules.QuranChallenge.Services.YouTubeOAuthClient>();
builder.Services.AddSingleton<Modules.QuranChallenge.Services.YouTubeUploadClient>();
builder.Services.AddSingleton<Modules.QuranChallenge.Services.YouTubeTokenVault>();
builder.Services.AddSingleton<Modules.QuranChallenge.Services.YouTubeConnectionService>();
builder.Services.AddSingleton<Modules.QuranChallenge.Services.YouTubePublishingClient>();
builder.Services.AddSingleton<Modules.QuranChallenge.Services.FacebookReelsUploadClient>();
builder.Services.AddSingleton<Modules.QuranChallenge.Services.TikTokApiClient>();
builder.Services.AddScoped<Modules.QuranChallenge.Services.TikTokConnectionService>();
builder.Services.AddScoped<Modules.QuranChallenge.Services.QuranVideoGenerator>();
builder.Services.AddScoped<Modules.QuranChallenge.Services.QuranVideoPublisher>();
builder.Services.AddScoped<Modules.QuranChallenge.Services.QuranFacebookPublisher>();
builder.Services.AddScoped<Modules.QuranChallenge.Services.QuranTikTokPublisher>();
builder.Services.AddHostedService<Modules.QuranChallenge.Jobs.QuranYouTubeScheduler>();
builder.Services.AddHostedService<Modules.QuranChallenge.Jobs.QuranFacebookScheduler>();
builder.Services.AddHostedService<Modules.QuranChallenge.Jobs.QuranTikTokPublishJob>();

// Keep production on RabbitMQ; local acceptance tests can use the same handler contracts synchronously.
var useInMemoryEventBus = builder.Configuration.GetValue<bool>("EventBus:UseInMemory");
if (useInMemoryEventBus && (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test")))
    builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
else
    builder.Services.AddSingleton<IEventBus, RabbitMQEventBus>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Enable tenancy middleware early in the pipeline
app.UseRouting();
app.UseMiddleware<WhatsAppGatewayWebhookAuthenticationMiddleware>();
app.UseMiddleware<TenantMiddleware>();
app.UseMiddleware<Modules.Advertising.API.AdvertisingExceptionMiddleware>();
app.UseCors("FrontendDev");
app.UseAuthentication();
app.UseAuthorization();

app.UseHangfireDashboard("/hangfire");

app.MapControllers();
app.MapHub<Modules.Conversations.Hubs.NotificationHub>("/hubs/notifications");

// Apply migrations automatically on startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    try
    {
        context.Database.Migrate();
        Console.WriteLine("✅ Database migrations applied successfully.");
        var projectSecretVault = scope.ServiceProvider.GetRequiredService<IProjectSecretVault>();
        var migratedProjectSecrets = await ProjectSecretMigration.ProtectLegacyGeminiKeysAsync(context, projectSecretVault);
        if (migratedProjectSecrets > 0)
            Console.WriteLine($"🔐 Protected {migratedProjectSecrets} legacy project AI credential(s).");

        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
            await DbSeeder.SeedAsync(context, passwordHasher, builder.Configuration);

        // Backfill only missing knowledge chunks. Do not re-embed all documents on every restart.
        Console.WriteLine("⏳ Checking Knowledge Base chunks...");
        var documents = await context.KnowledgeDocuments.IgnoreQueryFilters().ToListAsync();
        var geminiClient = scope.ServiceProvider.GetRequiredService<Modules.AI.Services.IGeminiClient>();
        int totalChunksCreated = 0;
        foreach (var doc in documents)
        {
            var hasChunks = await context.KnowledgeChunks
                .IgnoreQueryFilters()
                .AnyAsync(c => c.KnowledgeDocumentId == doc.Id);

            if (hasChunks)
            {
                continue;
            }

            var paragraphs = doc.Content.Split(new[] { "\r\n\r\n", "\n\n", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var currentChunk = new System.Text.StringBuilder();
            var chunks = new System.Collections.Generic.List<string>();

            foreach (var p in paragraphs)
            {
                var clean = p.Trim();
                if (string.IsNullOrEmpty(clean)) continue;

                if (currentChunk.Length + clean.Length > 800 && currentChunk.Length > 0)
                {
                    chunks.Add(currentChunk.ToString().Trim());
                    currentChunk.Clear();
                }
                currentChunk.AppendLine(clean);
            }
            if (currentChunk.Length > 0)
            {
                chunks.Add(currentChunk.ToString().Trim());
            }

            foreach (var chunkText in chunks)
            {
                try
                {
                    var embeddingFloats = await geminiClient.GenerateEmbeddingAsync(chunkText);
                    var embeddingVector = new Pgvector.Vector(embeddingFloats);

                    var chunk = new Modules.Brain.Domain.KnowledgeChunk
                    {
                        KnowledgeDocumentId = doc.Id,
                        ChunkText = chunkText,
                        Embedding = embeddingVector
                    };
                    context.KnowledgeChunks.Add(chunk);
                    totalChunksCreated++;
                }
                catch (Exception embEx)
                {
                    Console.WriteLine($"[Startup Re-chunker] Failed to generate embedding for chunk: {embEx.Message}");
                }
            }
            await context.SaveChangesAsync();
        }
        Console.WriteLine($"✅ Knowledge Base chunk check complete. Created {totalChunksCreated} missing chunks.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"⚠️ Database migration check/apply/seed failed: {ex.Message}");
    }
}

// Subscribe to integration events
using (var scope = app.Services.CreateScope())
{
    var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();
    var aiReplyWorkerCount = Math.Max(1, builder.Configuration.GetValue("AI:ReplyWorkerCount", 4));
    eventBus.Subscribe<Shared.Events.MessageAggregatedEvent, Modules.AI.Workers.AIReplyWorker>(aiReplyWorkerCount);
    eventBus.Subscribe<Shared.Events.AIReplyGeneratedEvent, Modules.WhatsApp.Workers.ReplySender>(consumerCount: 4);
    eventBus.Subscribe<Shared.Events.CRMUpdateSuggestedEvent, Modules.CRM.Workers.CRMWorker>();
    eventBus.Subscribe<Shared.Events.CustomerTagAddedEvent, Modules.Workflows.Workers.WorkflowWorker>();
    eventBus.Subscribe<Shared.Events.ConversationClosedEvent, Modules.Customers.Workers.CustomerMemoryWorker>();
    eventBus.Subscribe<Shared.Events.EntityIndexedEvent, Modules.Search.Workers.ElasticsearchIndexerWorker>();
    eventBus.Subscribe<Shared.Events.AIReplyGeneratedEvent, Modules.Facebook.Workers.FacebookReplySender>();
    eventBus.Subscribe<Shared.Events.KnowledgePublishedChangedEvent, Modules.Advertising.Workers.KnowledgeProjectionConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingProjectAssetChanged, Modules.Advertising.Workers.MediaProjectionConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingDealOutcomeChanged, Modules.Advertising.Workers.BusinessOutcomeConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingBookingOutcomeChanged, Modules.Advertising.Workers.BusinessOutcomeConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingQualifiedMessageChanged, Modules.Advertising.Workers.BusinessOutcomeConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingProjectLifecycleChanged, Modules.Advertising.Jobs.AdvertisingRetentionJob>();
    eventBus.Subscribe<Shared.Queue.ProjectAdvertisingContextChanged, Modules.Advertising.Workers.ProjectContextProjectionConsumer>();
    eventBus.Subscribe<Shared.Queue.ProjectAiConfigurationChanged, Modules.Advertising.Workers.ProjectAiConfigurationProjectionConsumer>();
    eventBus.Subscribe<Shared.Queue.OfferAvailabilityChanged, Modules.Advertising.Workers.OfferAvailabilityProjectionConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingKnowledgeChanged, Modules.Advertising.Workers.KnowledgeProjectionConsumer>();
    eventBus.Subscribe<Shared.Queue.CustomerAdvertisingConsentChanged, Modules.Advertising.Workers.ConsentProjectionConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingWhatsAppDestinationChanged, Modules.WhatsApp.Workers.WhatsAppInboundRouteConsumer>();
    eventBus.Subscribe<Shared.Queue.WhatsAppInboundMessageReceived, Modules.Conversations.Workers.WhatsAppInboundMessageConsumer>();
    eventBus.Subscribe<Shared.Queue.WhatsAppAttributionObserved, Modules.Advertising.Workers.WhatsAppAttributionObservationConsumer>();
    eventBus.Subscribe<Shared.Queue.WhatsAppAttributionObserved, Modules.Advertising.Workers.GatewayLeadObservationConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingAiWorkRequested, Modules.AI.Workers.AdvertisingAiWorkConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingAiWorkCompleted, Modules.Advertising.Workers.AdvertisingAiWorkResultConsumer>();
    eventBus.Subscribe<Shared.Queue.AdvertisingAuditRecorded, Shared.Audit.ElasticsearchAuditIndexer>();
}

// Register Hangfire Daily Analytics snapshot recurring job
using (var scope = app.Services.CreateScope())
{
    var manager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    manager.AddOrUpdate<Modules.Analytics.Jobs.DailyAnalyticsJob>("daily-analytics-snapshot", job => job.ExecuteAsync(), Cron.Daily);
    manager.RemoveIfExists("sales-ai-analyze-stale");
    manager.RemoveIfExists("sales-ai-daily-digest");
    manager.AddOrUpdate<Modules.Analytics.Jobs.SalesIntelligenceJob>("sales-ai-analyze-recent", job => job.AnalyzeRecentAsync(CancellationToken.None), "*/15 * * * *");
    manager.AddOrUpdate<Modules.Conversations.Jobs.UnansweredConversationRecoveryJob>("recover-unanswered-conversations", job => job.ExecuteAsync(CancellationToken.None), Cron.Minutely);
    manager.AddOrUpdate<Modules.Conversations.Jobs.ConversationReplyWindowDispatcher>("dispatch-conversation-reply-windows", job => job.DispatchAsync(CancellationToken.None), Cron.Minutely);
    manager.AddOrUpdate<Modules.Conversations.Jobs.WhatsAppLidContactRecoveryJob>("recover-whatsapp-lid-contacts", job => job.ExecuteAsync(CancellationToken.None), Cron.Minutely);
    manager.RemoveIfExists("talktips-trial-reminders");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-conversion-delivery", job => job.DeliverConversionsAsync(), Cron.Minutely);
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-campaign-bootstrap", job => job.BootstrapCampaignsAsync(), "*/5 * * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-spend-monitor", job => job.MonitorSpendAsync(), "*/5 * * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-provider-sync", job => job.SynchronizeAsync(), "*/10 * * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-insights", job => job.PullInsightsAsync(), "*/15 * * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-tracking-health", job => job.CheckTrackingAsync(), "*/15 * * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-decision-cycle", job => job.RunDecisionCycleAsync(), Cron.Hourly);
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-creative-fatigue", job => job.EvaluateFatigueAsync(), "17 */6 * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-daily-rebalance", job => job.RebalanceAsync(), "17 * * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-impact-review", job => job.ReviewImpactAsync(), "11 */2 * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-new-tests", job => job.CreateTestsAsync(), "*/5 * * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRecurringJobs>("ads-strategy-review", job => job.AnalyzeStrategyAsync(), "31 * * * *");
    manager.AddOrUpdate<Shared.Queue.IntegrationOutboxDispatcher>("integration-outbox", job => job.DispatchAsync(CancellationToken.None), Cron.Minutely);
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingRetentionJob>("ads-retention", job => job.CompactAsync(), "43 2 * * *");
    manager.AddOrUpdate<Modules.Advertising.Jobs.AdvertisingProjectionBackfillJob>("ads-projection-backfill", job => job.RunAsync(CancellationToken.None), Cron.Hourly);
    manager.AddOrUpdate<Shared.Audit.ElasticsearchAuditIndexer>("ads-audit-index-retry", job => job.RunPendingAsync(CancellationToken.None), Cron.Minutely);
    manager.AddOrUpdate<Modules.Content.Jobs.ContentAutomationJob>("content-daily-publishing", job => job.PublishDueAsync(), Cron.Minutely);
    manager.AddOrUpdate<Modules.Content.Jobs.ContentVideoJob>("content-video-recovery", job => job.RecoverAsync(), Cron.Minutely);

    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var activePlans = await context.ContentWeekPlans.IgnoreQueryFilters()
        .Where(plan => plan.Status == Modules.Content.Domain.ContentWeekPlanStatus.Generating
            || plan.Status == Modules.Content.Domain.ContentWeekPlanStatus.AwaitingApproval
            || plan.Status == Modules.Content.Domain.ContentWeekPlanStatus.Approved)
        .Select(plan => new { plan.ProjectId, plan.Status })
        .ToListAsync();
    var protectedPlanProjects = activePlans
        .Select(plan => plan.ProjectId)
        .Distinct()
        .ToArray();
    var projectsNeedingFirstPlan = await context.ContentAutomationSettings.IgnoreQueryFilters()
        .Where(settings => settings.HasApprovedStyle
            && settings.LastPublishedAtUtc != null
            && !protectedPlanProjects.Contains(settings.ProjectId))
        .Select(settings => settings.ProjectId)
        .ToListAsync();
    var backgroundJobs = scope.ServiceProvider.GetRequiredService<IBackgroundJobClient>();
    var projectsNeedingPlanWork = activePlans
        .Where(plan => plan.Status is Modules.Content.Domain.ContentWeekPlanStatus.Generating
            or Modules.Content.Domain.ContentWeekPlanStatus.AwaitingApproval)
        .Select(plan => plan.ProjectId)
        .Concat(projectsNeedingFirstPlan)
        .Distinct();
    foreach (var projectId in projectsNeedingPlanWork)
    {
        backgroundJobs.Enqueue<Modules.Content.Jobs.ContentAutomationJob>(job => job.GenerateWeeklyPlanAsync(projectId));
    }
}

app.Run();
