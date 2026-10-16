using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;

namespace Modules.GroupAppointments.Services;

public sealed record GroupParticipationRow(Guid CustomerId, string Name, string Phone,
    DateTime? SentAtUtc, string Membership);
public sealed record GroupParticipationReport(Guid GroupId, string GroupName, string MembershipAvailability,
    DateTime? CheckedAtUtc, IReadOnlyList<GroupParticipationRow> Rows);

public sealed class GroupParticipationService(AppDbContext db, IHttpClientFactory clients,
    IConfiguration configuration)
{
    private static readonly Regex InviteLink = new(
        @"https://chat\.whatsapp\.com/[A-Za-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<GroupParticipationReport?> GetAsync(Guid projectId, Guid groupId, CancellationToken ct)
    {
        var group = await db.GroupAppointments.AsNoTracking()
            .Include(g => g.Bookings)
            .SingleOrDefaultAsync(g => g.Id == groupId && g.ProjectId == projectId, ct);
        if (group is null) return null;
        var bookings = group.Bookings.Where(b => b.ProjectId == projectId).ToArray();
        if (bookings.Length == 0)
            return new(group.Id, group.Name, "empty", null, []);

        var accountId = group.WhatsAppAccountId ?? projectId;
        var accountValid = accountId == projectId || await db.WhatsAppAccounts.AsNoTracking()
            .AnyAsync(a => a.Id == accountId && a.ProjectId == projectId, ct);
        var customerIds = bookings.Select(b => b.CustomerId).Distinct().ToArray();
        var earliestBooking = bookings.Min(b => b.CreatedAt);
        var linkedMessages = await db.FollowUps.AsNoTracking()
            .Where(f => f.ProjectId == projectId && f.GroupAppointmentId == groupId
                && f.Type == "AppointmentReminder" && f.SentMessageId != null)
            .Select(f => f.SentMessageId!.Value).ToListAsync(ct);
        var sentMessages = await (
            from message in db.Messages.AsNoTracking()
            join conversation in db.Conversations.AsNoTracking() on message.ConversationId equals conversation.Id
            where conversation.ProjectId == projectId && customerIds.Contains(conversation.CustomerId)
                && conversation.Channel == "WhatsApp"
                && (conversation.WhatsAppAccountId == accountId
                    || (accountId == projectId && conversation.WhatsAppAccountId == null))
                && message.Direction == "Outgoing" && message.ExternalMessageId != null
                && message.ExternalMessageId != "" && message.Timestamp >= earliestBooking
                && message.Content.Contains("https://chat.whatsapp.com/")
            select new { message.Id, message.Content, message.Timestamp, conversation.CustomerId }
        ).ToListAsync(ct);
        var currentLink = group.WhatsAppGroupInviteLink?.Trim().TrimEnd('/');
        var invitations = sentMessages.Where(m => InviteLink.Matches(m.Content ?? "").Any(link =>
            linkedMessages.Contains(m.Id) || string.Equals(link.Value, currentLink, StringComparison.Ordinal))).ToArray();

        var people = bookings.GroupBy(b => GroupBookingPhone.Normalize(b.CustomerPhone) ?? b.Id.ToString()).ToArray();
        var phones = people.Select(p => GroupBookingPhone.Normalize(p.First().CustomerPhone))
            .Where(p => p is not null).Cast<string>().ToArray();
        var snapshot = accountValid && !string.IsNullOrWhiteSpace(group.WhatsAppGroupJid) && phones.Length <= 2000
            ? await ReadMembershipAsync(new(projectId, accountId == projectId ? null : accountId,
                group.WhatsAppGroupJid, phones), ct) : null;
        var statuses = snapshot?.Members?.GroupBy(m => m.Phone).ToDictionary(g => g.Key, g => g.First().Status)
            ?? new Dictionary<string, string>();
        var rows = people.Select(person =>
        {
            var booking = person.OrderBy(b => b.CreatedAt).First();
            var sentAt = invitations.Where(m => person.Any(b => b.CustomerId == m.CustomerId && m.Timestamp >= b.CreatedAt))
                .Select(m => (DateTime?)m.Timestamp).Max();
            var phone = GroupBookingPhone.Normalize(booking.CustomerPhone);
            var membership = phone != null && statuses.TryGetValue(phone, out var status)
                && status is "joined" or "not_joined" ? status : "unknown";
            return new GroupParticipationRow(booking.CustomerId, booking.CustomerName,
                phone ?? booking.CustomerPhone, sentAt, membership);
        }).OrderBy(r => r.Name).ToArray();
        var availability = snapshot == null ? "unavailable" : rows.Any(r => r.Membership == "unknown") ? "partial" : "ready";
        return new(group.Id, group.Name, availability, snapshot?.CheckedAtUtc, rows);
    }

    private async Task<MembershipSnapshot?> ReadMembershipAsync(MembershipRequest request, CancellationToken ct)
    {
        using var http = clients.CreateClient(nameof(GroupParticipationService));
        http.Timeout = TimeSpan.FromSeconds(12);
        var gatewayUrl = (configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000").TrimEnd('/');
        try
        {
            using var response = await http.PostAsJsonAsync($"{gatewayUrl}/api/whatsapp/group/membership",
                request, ct);
            if (!response.IsSuccessStatusCode) return null;
            var snapshot = await response.Content.ReadFromJsonAsync<MembershipSnapshot>(ct);
            if (snapshot?.CheckedAtUtc is null || snapshot.Members is null
                || snapshot.Members.Any(m => m is null || string.IsNullOrWhiteSpace(m.Phone)
                    || m.Status is not ("joined" or "not_joined" or "unknown"))
                || snapshot.Members.Select(m => m.Phone).Distinct().Count() != snapshot.Members.Length)
                return null;
            return snapshot;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested
            && exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private sealed record MembershipSnapshot(DateTime? CheckedAtUtc, MembershipEntry[]? Members);
    private sealed record MembershipEntry(string Phone, string Status);
    private sealed record MembershipRequest(Guid ProjectId, Guid? WhatsappAccountId, string GroupJid, string[] Phones);
}
