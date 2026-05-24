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

        public DbSet<Modules.Auth.Domain.User> Users { get; set; }
        public DbSet<Modules.Auth.Domain.RefreshToken> RefreshTokens { get; set; }
        public DbSet<Modules.Projects.Domain.Project> Projects { get; set; }
        public DbSet<Modules.Projects.Domain.ProjectSettings> ProjectSettings { get; set; }
        public DbSet<Modules.Conversations.Domain.Customer> Customers { get; set; }
        public DbSet<Modules.Conversations.Domain.Conversation> Conversations { get; set; }
        public DbSet<Modules.Conversations.Domain.Message> Messages { get; set; }
        public DbSet<Modules.CRM.Domain.FollowUp> FollowUps { get; set; }
        public DbSet<Modules.CRM.Domain.CRMUpdateProposal> CRMUpdateProposals { get; set; }
        public DbSet<Modules.Conversations.Domain.NotificationAlert> NotificationAlerts { get; set; }
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
        public DbSet<Modules.Media.Domain.Asset> Assets { get; set; }
        public DbSet<Modules.Media.Domain.AssetVariant> AssetVariants { get; set; }
        public DbSet<Modules.Audit.Domain.AuditLog> AuditLogs { get; set; }

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

            // Enable vector extension
            modelBuilder.HasPostgresExtension("vector");

            modelBuilder.Entity<Modules.Brain.Domain.KnowledgeChunk>()
                .Property(c => c.Embedding)
                .HasColumnType("vector(768)");

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

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            ApplyTenantAndAuditInfo();

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

            return result;
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
