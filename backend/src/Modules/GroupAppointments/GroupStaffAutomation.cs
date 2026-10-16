using System.Net.Http.Json;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Services;
using Modules.GroupAppointments.Domain;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;

namespace Modules.GroupAppointments.Services;

public sealed class GroupStaffAutomation(AppDbContext db, IHttpClientFactory clients,
    IConfiguration configuration, WhatsAppGatewaySessionClient sessions, WhatsAppAccountService accounts,
    ILogger<GroupStaffAutomation> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync()
    {
        var pending = await db.GroupStaffNotifications.IgnoreQueryFilters()
            .Where(n => n.SentAtUtc == null && n.CancelledAtUtc == null
                && (n.LastError == null || n.UpdatedAt <= DateTime.UtcNow.AddMinutes(-5)))
            .OrderBy(n => n.CreatedAt).Take(100).ToListAsync();
        foreach (var notification in pending)
            await DeliverAsync(notification);
        var groups = await db.GroupAppointments.IgnoreQueryFilters()
            .Where(g => g.IsActive && g.WhatsAppGroupJid != null && (g.FreeSessionDateTime ?? g.DateTime) >= DateTime.UtcNow)
            .ToListAsync();
        foreach (var group in groups)
            await SyncMembersAsync(group);
    }

    private async Task DeliverAsync(GroupStaffNotification notification)
    {
        var group = await db.GroupAppointments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(g => g.ProjectId == notification.ProjectId && g.Id == notification.GroupAppointmentId);
        var stillBooked = await db.GroupAppointmentBookings.IgnoreQueryFilters().AnyAsync(b =>
            b.ProjectId == notification.ProjectId && b.Id == notification.BookingId && b.GroupAppointmentId == notification.GroupAppointmentId);
        if (group is null || !stillBooked || group.SupervisorPhone != notification.RecipientPhone)
        {
            notification.CancelledAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return;
        }
        try
        {
            var session = await ResolveSenderAsync(notification, group);
            if (session is null)
            {
                notification.LastError = "GATEWAY_SESSION_UNAVAILABLE";
                await db.SaveChangesAsync();
                return;
            }
            await db.SaveChangesAsync();
            using var result = await PostAsync("send", new
            {
                projectId = group.ProjectId, whatsappAccountId = notification.WhatsAppAccountId,
                to = notification.RecipientPhone, message = notification.Content,
                idempotencyKey = $"group-staff:{notification.Id:N}", expectedConnectedAt = session.ConnectedAt
            });
            var messageId = result.RootElement.TryGetProperty("messageId", out var id) ? id.GetString() : null;
            if (string.IsNullOrWhiteSpace(messageId)) throw new JsonException("Missing provider message ID");
            notification.ProviderMessageId = messageId;
            notification.SentAtUtc = DateTime.UtcNow;
            notification.LastError = null;
        }
        catch (Exception exception) when (IsGatewayFailure(exception))
        {
            notification.LastError = exception.GetType().Name;
            logger.LogWarning("Group staff notification {Id} will retry: {Error}", notification.Id, notification.LastError);
        }
        await db.SaveChangesAsync();
    }

    private async Task<WhatsAppGatewaySessionStatus?> ResolveSenderAsync(
        GroupStaffNotification notification, GroupAppointment group)
    {
        var candidates = (await accounts.ListAsync(group.ProjectId))
            .Where(account => !notification.WhatsAppAccountId.HasValue || account.Id == notification.WhatsAppAccountId)
            .OrderByDescending(account => account.Id == group.WhatsAppAccountId);
        foreach (var account in candidates)
        {
            var session = await sessions.GetAsync(group.ProjectId, account.Id);
            if (!session.Connected || !session.ConnectedAt.HasValue) continue;
            notification.WhatsAppAccountId = account.Id;
            return session;
        }
        return null;
    }

    private async Task SyncMembersAsync(GroupAppointment group)
    {
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleOrDefaultAsync(s => s.ProjectId == group.ProjectId);
        if (settings is null || !settings.IsWhatsAppGroupAutomationEnabled || !group.WhatsAppAccountId.HasValue) return;
        var phones = GroupStaffNotifications.ParticipantPhones(settings, group);
        if (phones.Length == 0) return;
        try
        {
            var session = await sessions.GetAsync(group.ProjectId, group.WhatsAppAccountId.Value);
            if (!session.Connected || !session.ConnectedAt.HasValue) return;
            using var result = await PostAsync("group/members", new
            {
                projectId = group.ProjectId, whatsappAccountId = group.WhatsAppAccountId,
                groupJid = group.WhatsAppGroupJid, phones, expectedConnectedAt = session.ConnectedAt
            });
            group.StaffSyncError = result.RootElement.GetProperty("complete").GetBoolean() ? null : "PARTICIPANT_ADD_FAILED";
            group.StaffSyncedAtUtc = group.StaffSyncError is null ? DateTime.UtcNow : null;
        }
        catch (Exception exception) when (IsGatewayFailure(exception))
        {
            group.StaffSyncError = exception.GetType().Name;
            group.StaffSyncedAtUtc = null;
            logger.LogWarning("Group member sync {Id} will retry: {Error}", group.Id, group.StaffSyncError);
        }
        await db.SaveChangesAsync();
    }

    private async Task<JsonDocument> PostAsync(string route, object payload)
    {
        using var client = clients.CreateClient();
        var url = (configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000").TrimEnd('/');
        using var response = await client.PostAsJsonAsync($"{url}/api/whatsapp/{route}", payload);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static bool IsGatewayFailure(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException or JsonException;
}
