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

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(new Serilog.Formatting.Json.JsonFormatter(), "logs/audit.json", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();

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

builder.Services.AddHangfireServer();

// Dependency Injection registrations
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtService, JwtService>();

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
builder.Services.AddScoped<Modules.Conversations.Services.IAssignmentEngine, Modules.Conversations.Services.AssignmentEngine>();

// Register Media Services
builder.Services.AddSingleton<Modules.Media.Services.IMinIoStorageService, Modules.Media.Services.MinIoStorageService>();
builder.Services.AddScoped<Modules.Media.Services.IAssetService, Modules.Media.Services.AssetService>();
builder.Services.AddScoped<Modules.Media.Services.IImageTransformer, Modules.Media.Services.ImageTransformer>();
builder.Services.AddScoped<Modules.Media.Jobs.IMediaWorker, Modules.Media.Jobs.MediaWorker>();

// Register Audit Services
builder.Services.AddScoped<Modules.Audit.Services.IAuditService, Modules.Audit.Services.AuditService>();

// Register System Health Services
builder.Services.AddScoped<Modules.SystemHealth.Services.ISystemHealthService, Modules.SystemHealth.Services.SystemHealthService>();

// Register Gemini Client
builder.Services.AddSingleton<Modules.AI.Services.IGeminiClient, Modules.AI.Services.GeminiClient>();

// Register AI Marketing Brain
builder.Services.AddScoped<Modules.AI.Services.IAIMarketingBrain, Modules.AI.Services.AIMarketingBrain>();

// Register AI Company Brain
builder.Services.AddScoped<Modules.Brain.Services.IAICompanyBrain, Modules.Brain.Services.AICompanyBrain>();
builder.Services.AddScoped<Modules.Brain.Services.IKnowledgeBaseService, Modules.Brain.Services.KnowledgeBaseService>();

// Register Risk Analyzer for Approvals
builder.Services.AddScoped<Modules.Approvals.Services.IRiskAnalyzer, Modules.Approvals.Services.RiskAnalyzer>();

// Register Campaigns Services
builder.Services.AddScoped<Modules.Campaigns.Application.Services.ICampaignAIService, Modules.Campaigns.Application.Services.CampaignAIService>();
builder.Services.AddScoped<Modules.Campaigns.Jobs.CampaignSenderJob>();

// Register Analytics Services
builder.Services.AddScoped<Modules.Analytics.Application.Services.IAnalyticsEngine, Modules.Analytics.Application.Services.AnalyticsEngine>();
builder.Services.AddScoped<Modules.Analytics.Jobs.DailyAnalyticsJob>();

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

// Register Event Handlers
builder.Services.AddScoped<Modules.AI.Workers.AIReplyWorker>();
builder.Services.AddScoped<Modules.WhatsApp.Workers.ReplySender>();
builder.Services.AddScoped<Modules.CRM.Services.ICRMAutoUpdateEngine, Modules.CRM.Services.CRMAutoUpdateEngine>();
builder.Services.AddScoped<Modules.CRM.Workers.CRMWorker>();
builder.Services.AddScoped<Modules.Workflows.Services.IWorkflowEngine, Modules.Workflows.Services.WorkflowEngine>();
builder.Services.AddScoped<Modules.Workflows.Workers.WorkflowWorker>();

// Register CRM/Follow-up Hosted Services
builder.Services.AddHostedService<Modules.CRM.Services.FollowUpScheduler>();

// Register RabbitMQ Event Bus as a singleton
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
app.UseMiddleware<TenantMiddleware>();
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
        await DbSeeder.SeedAsync(context, passwordHasher);
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
    eventBus.Subscribe<Shared.Events.MessageAggregatedEvent, Modules.AI.Workers.AIReplyWorker>();
    eventBus.Subscribe<Shared.Events.AIReplyGeneratedEvent, Modules.WhatsApp.Workers.ReplySender>();
    eventBus.Subscribe<Shared.Events.CRMUpdateSuggestedEvent, Modules.CRM.Workers.CRMWorker>();
    eventBus.Subscribe<Shared.Events.CustomerTagAddedEvent, Modules.Workflows.Workers.WorkflowWorker>();
    eventBus.Subscribe<Shared.Events.ConversationClosedEvent, Modules.Customers.Workers.CustomerMemoryWorker>();
    eventBus.Subscribe<Shared.Events.EntityIndexedEvent, Modules.Search.Workers.ElasticsearchIndexerWorker>();
}

// Register Hangfire Daily Analytics snapshot recurring job
using (var scope = app.Services.CreateScope())
{
    var manager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    manager.AddOrUpdate<Modules.Analytics.Jobs.DailyAnalyticsJob>("daily-analytics-snapshot", job => job.ExecuteAsync(), Cron.Daily);
}

app.Run();
