using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record ReadinessItem(string Key, string Label, bool Ready, string? Reason = null);
public sealed record AdvertisingReadiness(bool Ready, IReadOnlyList<ReadinessItem> Items);

public sealed class AdvertisingReadinessService(AppDbContext db)
{
    public async Task<AdvertisingReadiness> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var connection = await db.AdvertisingConnections.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
        var envelope = await db.AutonomyEnvelopes.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId && x.State == EnvelopeState.Active, cancellationToken);
        var offer = await db.AdvertisingOffers.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.State == "Eligible", cancellationToken);
        var trackingIncident = await db.TrackingIncidents.AsNoTracking().AnyAsync(x => x.ProjectId == projectId && x.State != IncidentState.Recovered, cancellationToken);

        var items = new List<ReadinessItem>
        {
            new("connection", "ربط حساب إعلانات وصفحة Facebook", connection?.State == AdvertisingConnectionState.Ready, connection is null ? "لم يتم الربط بعد" : connection.LastErrorSummary),
            new("offer", "عرض موثّق من قاعدة المعرفة", offer, offer ? null : "راجع العرض والسعر والوجهة"),
            new("tracking", "اختبار تتبع التحويلات", !trackingIncident && connection?.DatasetExternalId is not null, trackingIncident ? "يوجد عطل تتبع مفتوح" : null),
            new("budget", "سقف يومي مفوّض", envelope is not null && envelope.DailyCap > 0, envelope is null ? "حدد السقف وحدود التشغيل" : null),
        };
        return new(items.All(x => x.Ready), items);
    }
}
