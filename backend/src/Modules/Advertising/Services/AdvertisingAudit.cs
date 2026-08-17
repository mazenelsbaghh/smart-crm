using System.Text.Json;
using Modules.Audit.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public static class AdvertisingMetrics
{
    public const string Spend = "ads.spend";
    public const string Revenue = "ads.revenue";
    public const string Roas = "ads.roas";
    public const string QualifiedLeads = "ads.qualified_leads";
    public const string Purchases = "ads.purchases";
    public const string Commands = "ads.commands";
    public const string EmergencyStops = "ads.emergency_stops";
}

public static class AdvertisingAudit
{
    public static void Add(AppDbContext db, Guid projectId, string action, string entityType, Guid entityId, object safeState, Guid? userId = null)
    {
        db.AuditLogs.Add(new AuditLog { ProjectId = projectId, UserId = userId, Action = action, EntityType = entityType,
            EntityId = entityId.ToString(), NewState = AdvertisingLogSanitizer.Redact(JsonSerializer.Serialize(safeState)), Timestamp = DateTime.UtcNow });
    }
}
