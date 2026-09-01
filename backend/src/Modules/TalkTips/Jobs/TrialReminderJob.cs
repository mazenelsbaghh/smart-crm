using Microsoft.EntityFrameworkCore;
using Modules.TalkTips.Domain;
using Modules.TalkTips.Services;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;

namespace Modules.TalkTips.Jobs;

public sealed class TrialReminderJob(
    AppDbContext dbContext,
    TalkTipsTrialStatusClient trialStatusClient,
    WhatsAppAccountService whatsAppAccounts,
    ILogger<TrialReminderJob> logger)
{
    private const int BatchSize = 50;

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var reminders = await dbContext.TalkTipsTrialReminders
            .IgnoreQueryFilters()
            .Where(reminder => reminder.CompletedAtUtc == null
                && dbContext.ProjectSettings.IgnoreQueryFilters().Any(settings => settings.ProjectId == reminder.ProjectId && settings.IsTalkTipsTrialGateEnabled)
                && (reminder.DayReminderSentAtUtc == null && reminder.FirstPromptedAtUtc <= now.AddDays(-1)
                    || reminder.FiveMinuteReminderSentAtUtc == null && reminder.FirstPromptedAtUtc <= now.AddMinutes(-5)
                    || reminder.OneMinuteReminderSentAtUtc == null && reminder.FirstPromptedAtUtc <= now.AddMinutes(-1)))
            .OrderBy(reminder => reminder.FirstPromptedAtUtc)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var reminder in reminders)
        {
            var customer = await dbContext.Customers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(item => item.Id == reminder.CustomerId, cancellationToken);
            if (customer is null || customer.IsBlacklisted) continue;

            if (await trialStatusClient.HasTriedAsync(customer.PhoneNumber, cancellationToken))
            {
                reminder.CompletedAtUtc = now;
                continue;
            }

            var (stage, message) = NextReminderMessage(reminder, now);
            var whatsAppAccount = await whatsAppAccounts.GetDefaultAsync(
                reminder.ProjectId,
                cancellationToken);
            var followUpId = DeterministicFollowUpId(reminder.Id, stage);
            if (!await dbContext.FollowUps.IgnoreQueryFilters()
                .AnyAsync(followUp => followUp.Id == followUpId, cancellationToken))
            {
                dbContext.FollowUps.Add(new Modules.CRM.Domain.FollowUp
                {
                    Id = followUpId,
                    ProjectId = reminder.ProjectId,
                    CustomerId = reminder.CustomerId,
                    WhatsAppAccountId = whatsAppAccount.Id,
                    Channel = "WhatsApp",
                    DueDate = now,
                    Status = "Pending",
                    Type = "Nurturing",
                    Notes = message
                });
            }
        }

        if (reminders.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Processed {Count} TalkTips trial reminders.", reminders.Count);
        }
    }

    private static (string Stage, string Message) NextReminderMessage(TrialReminder reminder, DateTime now)
    {
        if (reminder.DayReminderSentAtUtc is null && reminder.FirstPromptedAtUtc <= now.AddDays(-1))
        {
            reminder.DayReminderSentAtUtc = now;
            reminder.OneMinuteReminderSentAtUtc ??= now;
            reminder.FiveMinuteReminderSentAtUtc ??= now;
            return ("day", "لسه مستنيينك تجرّب منصة TalkTips التفاعلية 👋\nhttps://talktips-academy.com/ar/try\n\nبعد ما تخلص ابعتلنا رسالة ونكمل معاك.");
        }

        if (reminder.FiveMinuteReminderSentAtUtc is null && reminder.FirstPromptedAtUtc <= now.AddMinutes(-5))
        {
            reminder.FiveMinuteReminderSentAtUtc = now;
            reminder.OneMinuteReminderSentAtUtc ??= now;
            return ("five-minute", "فكّرك تجرّب منصة TalkTips التفاعلية من هنا 👋\nhttps://talktips-academy.com/ar/try");
        }

        reminder.OneMinuteReminderSentAtUtc = now;
        return ("one-minute", "جرّب المنصة التفاعلية أولاً، وبعدها ابعتلنا أي رسالة ونكمل معاك 👋\nhttps://talktips-academy.com/ar/try");
    }

    private static Guid DeterministicFollowUpId(Guid reminderId, string stage)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"talktips:{reminderId:N}:{stage}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
