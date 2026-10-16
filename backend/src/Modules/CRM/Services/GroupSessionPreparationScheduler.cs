using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Domain;
using Shared.Infrastructure;

namespace Modules.CRM.Services;

internal sealed class GroupSessionPreparationScheduler(AppDbContext db, HashSet<Guid>? knownIds = null)
{
    public const string VideoType = "GroupPreparationVideo";
    public const string SiteType = "GroupPreparationSite";
    public const string ContextMarker = "SESSION_PREPARATION_SCHEDULE_ENABLED";
    public const string Instructions = """
        CURRENT BOOKING CONFIRMATION AND SESSION PREPARATION RULE (overrides older cached instructions and knowledge):
        All booked customers, online AND offline, get a WhatsApp group for their session. Explain that the group is for watching the preparation video and doing the level assessment before the session.
        The system sends the preparation video TWO DAYS before the free session, and a website reminder ONE DAY before it. Use the free-session date when available, otherwise the booked session date. After a booking succeeds, the system appends the group explanation and then the immediate 'جرب الويبسايت' invitation with the trial link as the FINAL paragraph/message of the booking confirmation. Do not duplicate these paragraphs or append another CTA/signature after the trial link. Do not volunteer either preparation link before booking succeeds. Do not claim the customer has already joined the group or received a link unless delivery is confirmed.
        The video and website are sent PRIVATELY to EACH booked person, including anyone booked by their friend. For a companion's booking, request their name and WhatsApp number; the system verifies WhatsApp registration before booking. Never confirm an unverified companion's booking or describe a verification outage as a wrong phone number.
        """;
    public const string Confirmation = "كل شخص اتأكد حجزه هيبقى ليه جروب للسيشن، سواء أونلاين أو في السنتر، وهيوصله لينك الجروب عشان يدخل ويجهز نفسه لفيديو التحضير وتحديد المستوى. الفيديو هيتبعت قبل السيشن بيومين، وتذكير السايت قبلها بيوم، على الخاص لكل شخص.";

    public static bool IsPreparation(FollowUp followUp) => followUp.GroupAppointmentBookingId.HasValue
        && followUp.Type is "AppointmentReminder" or VideoType or SiteType;

    public static IQueryable<FollowUp> ExcludePreparation(IQueryable<FollowUp> query) => query.Where(followUp =>
        !followUp.GroupAppointmentBookingId.HasValue
        || (followUp.Type != "AppointmentReminder" && followUp.Type != VideoType && followUp.Type != SiteType));

    public async Task ScheduleAsync(GroupAppointment group, GroupAppointmentBooking booking,
        Guid accountId, FallbackMessageSettings templates, Guid inviteId, DateTime nowUtc)
    {
        var sessionUtc = group.FreeSessionDateTime ?? group.DateTime;
        if (sessionUtc <= nowUtc) return;
        var zone = TimezoneHelper.GetTimeZone("Africa/Cairo");
        var sessionLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(sessionUtc, DateTimeKind.Utc), zone);
        var videoId = Id(group.Id, booking.Id, VideoType, sessionUtc);
        var hasVideo = !string.IsNullOrWhiteSpace(templates.GroupPreparationVideo);
        if (hasVideo)
            await AddAsync(VideoType, templates.GroupPreparationVideo, 2, videoId, inviteId);
        if (!string.IsNullOrWhiteSpace(templates.GroupPreparationSite))
            await AddAsync(SiteType, templates.GroupPreparationSite, 1,
                Id(group.Id, booking.Id, SiteType, sessionUtc), hasVideo ? videoId : inviteId);

        async Task AddAsync(string type, string template, int daysBefore, Guid id, Guid? predecessorId)
        {
            if ((knownIds is not null && knownIds.Contains(id))
                || db.FollowUps.Local.Any(followUp => followUp.Id == id)
                || (knownIds is null && await db.FollowUps.IgnoreQueryFilters().AnyAsync(followUp => followUp.Id == id))) return;
            var dueUtc = TimeZoneInfo.ConvertTimeToUtc(sessionLocal.AddDays(-daysBefore), zone);
            db.FollowUps.Add(new FollowUp
            {
                Id = id, ProjectId = group.ProjectId, CustomerId = booking.CustomerId,
                GroupAppointmentId = group.Id, GroupAppointmentBookingId = booking.Id,
                WhatsAppAccountId = accountId, Channel = "WhatsApp", Type = type,
                AppointmentTime = sessionUtc, DueDate = dueUtc < nowUtc ? nowUtc : dueUtc,
                DependsOnFollowUpId = predecessorId, Status = "Pending", Tone = "Exact",
                Notes = template.Replace("{customerName}", booking.CustomerName)
                    .Replace("{groupName}", group.Name).Replace("{waveName}", group.Name)
            });
            knownIds?.Add(id);
        }
    }

    private static Guid Id(Guid groupId, Guid bookingId, string type, DateTime sessionUtc) => new(
        SHA256.HashData(Encoding.UTF8.GetBytes($"group-preparation:{groupId:N}:{bookingId:N}:{type}:{sessionUtc.Ticks}"))
            .AsSpan(0, 16));
}
