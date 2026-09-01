using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Modules.CRM.Domain;
using Shared.Infrastructure;

namespace Modules.CRM.Services;

public sealed record PendingAutomationFollowUpRequest(
    Guid ProjectId,
    Guid CustomerId,
    string ActiveSlotKey,
    DateTime DueDate,
    string Notes,
    string Type = "Nurturing",
    Guid? ConversationId = null,
    Guid? WhatsAppAccountId = null,
    string? Channel = null,
    DateTime? AppointmentTime = null,
    string Tone = "Default");

/// <summary>
/// Serializes changes to an active automation slot and keeps at most one
/// Pending/Processing follow-up for it. Callers may already own a transaction.
/// </summary>
public sealed class AutomationFollowUpService(AppDbContext dbContext)
{
    public async Task<FollowUp> UpsertPendingAutomationFollowUpAsync(
        PendingAutomationFollowUpRequest request,
        CancellationToken cancellationToken = default)
    {
        IDbContextTransaction? ownedTransaction = null;
        if (dbContext.Database.IsRelational() && dbContext.Database.CurrentTransaction is null)
            ownedTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            if (string.Equals(
                dbContext.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
            {
                var lockIdentity = $"follow-up-automation:{request.ProjectId:N}:{request.ActiveSlotKey}";
                await dbContext.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock(hashtextextended({lockIdentity}, 0))",
                    cancellationToken);
            }

            var active = await dbContext.FollowUps.IgnoreQueryFilters()
                .Where(followUp => followUp.ProjectId == request.ProjectId
                    && followUp.ActiveAutomationSlotKey == request.ActiveSlotKey
                    && (followUp.Status == "Pending" || followUp.Status == "Processing"))
                .OrderByDescending(followUp => followUp.Status == "Processing")
                .ThenByDescending(followUp => followUp.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            var pendingInSource = await dbContext.FollowUps.IgnoreQueryFilters()
                .Where(followUp => followUp.ProjectId == request.ProjectId
                    && followUp.CustomerId == request.CustomerId
                    && followUp.Status == "Pending"
                    && (request.ConversationId.HasValue
                        ? followUp.ConversationId == request.ConversationId
                        : !followUp.ConversationId.HasValue
                            && followUp.Channel == request.Channel
                            && (request.Channel != "WhatsApp"
                                || (followUp.WhatsAppAccountId ?? followUp.ProjectId)
                                    == (request.WhatsAppAccountId ?? request.ProjectId))))
                .ToListAsync(cancellationToken);
            foreach (var obsolete in pendingInSource.Where(followUp => followUp.Id != active?.Id))
                obsolete.Status = "Bypassed";

            if (active is null)
            {
                active = new FollowUp
                {
                    ProjectId = request.ProjectId,
                    CustomerId = request.CustomerId,
                    ConversationId = request.ConversationId,
                    WhatsAppAccountId = request.WhatsAppAccountId,
                    Channel = request.Channel,
                    ActiveAutomationSlotKey = request.ActiveSlotKey,
                    DueDate = request.DueDate,
                    Notes = request.Notes,
                    Type = request.Type,
                    AppointmentTime = request.AppointmentTime,
                    Tone = request.Tone,
                    Status = "Pending"
                };
                dbContext.FollowUps.Add(active);
            }
            else if (active.Status == "Pending")
            {
                active.CustomerId = request.CustomerId;
                active.ConversationId = request.ConversationId;
                active.WhatsAppAccountId = request.WhatsAppAccountId;
                active.Channel = request.Channel;
                active.DueDate = request.DueDate;
                active.Notes = request.Notes;
                active.Type = request.Type;
                active.AppointmentTime = request.AppointmentTime;
                active.Tone = request.Tone;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);
            return active;
        }
        catch
        {
            if (ownedTransaction is not null)
                await ownedTransaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }
}
