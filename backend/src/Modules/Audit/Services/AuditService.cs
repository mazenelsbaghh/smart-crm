using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Elastic.Clients.Elasticsearch;
using Shared.Infrastructure;
using Modules.Audit.Domain;

namespace Modules.Audit.Services
{
    public interface IAuditService
    {
        Task LogAuditAsync(Guid projectId, Guid? userId, string action, string entityType, string entityId, string? originalState, string? newState, string? ipAddress);
    }

    public class AuditService : IAuditService
    {
        private readonly AppDbContext _context;
        private readonly ElasticsearchClient _elasticClient;
        private readonly ILogger<AuditService> _logger;

        public AuditService(
            AppDbContext context,
            ElasticsearchClient elasticClient,
            ILogger<AuditService> logger)
        {
            _context = context;
            _elasticClient = elasticClient;
            _logger = logger;
        }

        public async Task LogAuditAsync(Guid projectId, Guid? userId, string action, string entityType, string entityId, string? originalState, string? newState, string? ipAddress)
        {
            var auditLog = new AuditLog
            {
                ProjectId = projectId,
                UserId = userId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                OriginalState = originalState,
                NewState = newState,
                IPAddress = ipAddress,
                Timestamp = DateTime.UtcNow
            };

            // 1. Save to Database (PostgreSQL) - Critical path
            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync();

            // 2. Log structured json using Serilog
            _logger.LogInformation("Structured Audit Log: {@AuditLog}", auditLog);

            // 3. Index in Elasticsearch - Non-critical path
            try
            {
                // Index to "smart_whatsapp_audit" index
                var response = await _elasticClient.IndexAsync(auditLog, idx => idx.Index("smart_whatsapp_audit"));
                if (!response.IsValidResponse)
                {
                    _logger.LogWarning("Failed to index audit log in Elasticsearch: {ServerError}", response.ElasticsearchServerError?.Error?.Reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send audit log to Elasticsearch.");
            }
        }
    }
}
