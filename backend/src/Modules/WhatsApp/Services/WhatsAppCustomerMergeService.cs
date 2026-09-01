using Microsoft.EntityFrameworkCore;
using Modules.Campaigns.Domain;
using Modules.Conversations.Domain;
using Modules.GroupAppointments.Services;
using Shared.Infrastructure;

namespace Modules.WhatsApp.Services;

/// <summary>
/// Collapses an account-scoped provisional LID customer into the project-wide
/// phone customer without losing conversation, CRM, booking, or attribution history.
/// </summary>
public sealed class WhatsAppCustomerMergeService(AppDbContext dbContext)
{
    public async Task<Customer?> ResolveByPhoneAsync(
        Guid projectId,
        string normalizedPhone,
        CancellationToken cancellationToken = default)
    {
        normalizedPhone = GroupBookingPhone.Normalize(normalizedPhone)
            ?? throw new ArgumentException("A canonical WhatsApp phone is required.", nameof(normalizedPhone));
        var identity = await dbContext.WhatsAppPhoneCustomerIdentities.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId
                && item.NormalizedPhone == normalizedPhone, cancellationToken);
        if (identity is not null)
            return await dbContext.Customers.IgnoreQueryFilters()
                .FirstOrDefaultAsync(customer => customer.ProjectId == projectId
                    && customer.Id == identity.CustomerId, cancellationToken);

        var existing = (await PhoneCustomersAsync(projectId, normalizedPhone, cancellationToken))
            .FirstOrDefault();
        return existing is null
            ? null
            : await BindPhoneAsync(projectId, existing.Id, normalizedPhone, cancellationToken);
    }

    public async Task<Customer> BindPhoneAsync(
        Guid projectId,
        Guid candidateCustomerId,
        string normalizedPhone,
        CancellationToken cancellationToken = default)
    {
        normalizedPhone = GroupBookingPhone.Normalize(normalizedPhone)
            ?? throw new ArgumentException("A canonical WhatsApp phone is required.", nameof(normalizedPhone));
        if (!dbContext.Database.IsRelational())
            return await BindPhoneCoreAsync(
                projectId,
                candidateCustomerId,
                normalizedPhone,
                cancellationToken);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            await AcquirePhoneMergeLockAsync(projectId, normalizedPhone, cancellationToken);
            return await BindPhoneCoreAsync(
                projectId,
                candidateCustomerId,
                normalizedPhone,
                cancellationToken);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        await AcquirePhoneMergeLockAsync(projectId, normalizedPhone, cancellationToken);
        var result = await BindPhoneCoreAsync(
            projectId,
            candidateCustomerId,
            normalizedPhone,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<Customer> BindPhoneCoreAsync(
        Guid projectId,
        Guid candidateCustomerId,
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        var existingIdentity = await dbContext.WhatsAppPhoneCustomerIdentities.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId
                && item.NormalizedPhone == normalizedPhone, cancellationToken);
        if (existingIdentity is not null)
            return existingIdentity.CustomerId == candidateCustomerId
                ? await CanonicalCustomerAsync(projectId, candidateCustomerId, cancellationToken)
                : await MergeAsync(projectId, candidateCustomerId, existingIdentity.CustomerId, cancellationToken);

        var phoneCustomers = await PhoneCustomersAsync(projectId, normalizedPhone, cancellationToken);
        var canonical = phoneCustomers.FirstOrDefault()
            ?? await CanonicalCustomerAsync(projectId, candidateCustomerId, cancellationToken);
        if (canonical.Id != candidateCustomerId)
            canonical = await MergeAsync(projectId, candidateCustomerId, canonical.Id, cancellationToken);
        foreach (var duplicate in phoneCustomers.Where(customer => customer.Id != canonical.Id).ToArray())
            canonical = await MergeAsync(projectId, duplicate.Id, canonical.Id, cancellationToken);
        canonical.PhoneNumber = normalizedPhone;

        var phoneIdentity = new Modules.WhatsApp.Domain.WhatsAppPhoneCustomerIdentity
        {
            Id = DeterministicId($"whatsapp-phone:{projectId:N}:{normalizedPhone}"),
            ProjectId = projectId,
            CustomerId = canonical.Id,
            NormalizedPhone = normalizedPhone
        };
        dbContext.WhatsAppPhoneCustomerIdentities.Add(phoneIdentity);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return canonical;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(phoneIdentity).State = EntityState.Detached;
            var winner = await dbContext.WhatsAppPhoneCustomerIdentities.IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(item => item.ProjectId == projectId
                    && item.NormalizedPhone == normalizedPhone, cancellationToken);
            if (winner is null) throw;
            return winner.CustomerId == canonical.Id
                ? canonical
                : await MergeAsync(projectId, canonical.Id, winner.CustomerId, cancellationToken);
        }
    }

    private Task AcquirePhoneMergeLockAsync(
        Guid projectId,
        string normalizedPhone,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                dbContext.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal))
            return Task.CompletedTask;

        var lockIdentity = $"whatsapp-phone-customer:{projectId:N}:{normalizedPhone}";
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockIdentity}, 0))",
            cancellationToken);
    }

    public async Task<Customer> MergeAsync(
        Guid projectId,
        Guid sourceCustomerId,
        Guid targetCustomerId,
        CancellationToken cancellationToken = default)
    {
        var target = await dbContext.Customers.IgnoreQueryFilters()
            .FirstAsync(customer => customer.ProjectId == projectId
                && customer.Id == targetCustomerId, cancellationToken);
        if (sourceCustomerId == targetCustomerId) return target;

        var source = await dbContext.Customers.IgnoreQueryFilters()
            .FirstOrDefaultAsync(customer => customer.ProjectId == projectId
                && customer.Id == sourceCustomerId, cancellationToken);
        if (source is null) return target;

        MergeCustomerProfile(source, target);

        await MergeConversationsAsync(
            projectId,
            sourceCustomerId,
            targetCustomerId,
            cancellationToken);
        foreach (var item in await dbContext.FollowUps.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.CustomerTasks.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.CRMUpdateProposals.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.TalkTipsTrialReminders.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.Deals.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.ConversationSalesAnalyses.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.GroupAppointmentBookings.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken))
        {
            item.CustomerId = targetCustomerId;
            item.CustomerPhone = target.PhoneNumber;
            if (!string.IsNullOrWhiteSpace(target.Name)) item.CustomerName = target.Name;
        }
        foreach (var item in await dbContext.WorkflowExecutionLogs.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.WhatsAppCustomerIdentities.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.WhatsAppPhoneCustomerIdentities.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.AdvertisingAttributionObservations.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;
        foreach (var item in await dbContext.AdvertisingAttributionContexts.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken)) item.CustomerId = targetCustomerId;

        await MergeMemoriesAsync(projectId, sourceCustomerId, targetCustomerId, cancellationToken);
        await MergeCampaignRecipientsAsync(projectId, sourceCustomerId, targetCustomerId, cancellationToken);
        await MergeAdvertisingConsentAsync(projectId, sourceCustomerId, targetCustomerId, cancellationToken);

        dbContext.Customers.Remove(source);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return target;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another webhook completed the same deterministic merge. Its atomic
            // commit is authoritative; discard stale tracked state and continue.
            dbContext.ChangeTracker.Clear();
            return await dbContext.Customers.IgnoreQueryFilters()
                .AsNoTracking()
                .FirstAsync(customer => customer.ProjectId == projectId
                    && customer.Id == targetCustomerId, cancellationToken);
        }
    }

    private async Task MergeConversationsAsync(
        Guid projectId,
        Guid sourceCustomerId,
        Guid targetCustomerId,
        CancellationToken cancellationToken)
    {
        var sourceConversations = await dbContext.Conversations.IgnoreQueryFilters()
            .Where(conversation => conversation.ProjectId == projectId
                && conversation.CustomerId == sourceCustomerId)
            .ToListAsync(cancellationToken);
        var targetConversations = await dbContext.Conversations.IgnoreQueryFilters()
            .Where(conversation => conversation.ProjectId == projectId
                && conversation.CustomerId == targetCustomerId)
            .ToListAsync(cancellationToken);

        foreach (var source in sourceConversations)
        {
            var target = source.Status == "Closed"
                ? null
                : targetConversations
                    .Where(candidate => candidate.Status != "Closed"
                        && candidate.Channel == source.Channel
                        && candidate.WhatsAppAccountId == source.WhatsAppAccountId
                        && candidate.WhatsAppDestinationId == source.WhatsAppDestinationId)
                    .OrderByDescending(candidate => candidate.LastMessageTimestamp)
                    .FirstOrDefault();
            if (target is null)
            {
                source.CustomerId = targetCustomerId;
                targetConversations.Add(source);
                continue;
            }

            foreach (var message in await dbContext.Messages.IgnoreQueryFilters()
                .Where(message => message.ConversationId == source.Id)
                .ToListAsync(cancellationToken)) message.ConversationId = target.Id;
            foreach (var followUp in await dbContext.FollowUps.IgnoreQueryFilters()
                .Where(followUp => followUp.ProjectId == projectId
                    && followUp.ConversationId == source.Id)
                .ToListAsync(cancellationToken))
            {
                followUp.ConversationId = target.Id;
                followUp.CustomerId = targetCustomerId;
                followUp.WhatsAppAccountId ??= target.WhatsAppAccountId;
            }
            foreach (var observation in await dbContext.AdvertisingAttributionObservations.IgnoreQueryFilters()
                .Where(observation => observation.ProjectId == projectId
                    && observation.ConversationId == source.Id)
                .ToListAsync(cancellationToken)) observation.ConversationId = target.Id;
            foreach (var touch in await dbContext.AdvertisingAttributionTouches.IgnoreQueryFilters()
                .Where(touch => touch.ProjectId == projectId
                    && touch.ConversationId == source.Id)
                .ToListAsync(cancellationToken)) touch.ConversationId = target.Id;

            await MergeAttributionContextAsync(projectId, source.Id, target.Id, cancellationToken);
            await MergeSalesAnalysisAsync(projectId, source.Id, target.Id, targetCustomerId, cancellationToken);

            if (source.LastMessageTimestamp > target.LastMessageTimestamp)
                target.LastMessageTimestamp = source.LastMessageTimestamp;
            if (source.WhatsAppDeliveryUnknownAt.HasValue
                && (!target.WhatsAppDeliveryUnknownAt.HasValue
                    || source.WhatsAppDeliveryUnknownAt.Value > target.WhatsAppDeliveryUnknownAt.Value))
            {
                target.WhatsAppDeliveryUnknownAt = source.WhatsAppDeliveryUnknownAt;
                target.WhatsAppDeliveryUnknownKey = source.WhatsAppDeliveryUnknownKey;
            }
            if (source.Status == "Open") target.Status = "Open";
            dbContext.Conversations.Remove(source);
        }
    }

    private async Task MergeAttributionContextAsync(
        Guid projectId,
        Guid sourceConversationId,
        Guid targetConversationId,
        CancellationToken cancellationToken)
    {
        var source = await dbContext.AdvertisingAttributionContexts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(context => context.ProjectId == projectId
                && context.ConversationId == sourceConversationId, cancellationToken);
        if (source is null) return;
        var target = await dbContext.AdvertisingAttributionContexts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(context => context.ProjectId == projectId
                && context.ConversationId == targetConversationId, cancellationToken);
        if (target is null)
        {
            source.ConversationId = targetConversationId;
            return;
        }
        target.FirstObservedAtUtc = target.FirstObservedAtUtc < source.FirstObservedAtUtc
            ? target.FirstObservedAtUtc
            : source.FirstObservedAtUtc;
        target.LastObservedAtUtc = target.LastObservedAtUtc > source.LastObservedAtUtc
            ? target.LastObservedAtUtc
            : source.LastObservedAtUtc;
        target.ObservationCount += source.ObservationCount;
        target.ValidReferralCount += source.ValidReferralCount;
        dbContext.AdvertisingAttributionContexts.Remove(source);
    }

    private async Task MergeSalesAnalysisAsync(
        Guid projectId,
        Guid sourceConversationId,
        Guid targetConversationId,
        Guid targetCustomerId,
        CancellationToken cancellationToken)
    {
        var source = await dbContext.ConversationSalesAnalyses.IgnoreQueryFilters()
            .FirstOrDefaultAsync(analysis => analysis.ProjectId == projectId
                && analysis.ConversationId == sourceConversationId, cancellationToken);
        if (source is null) return;
        var target = await dbContext.ConversationSalesAnalyses.IgnoreQueryFilters()
            .FirstOrDefaultAsync(analysis => analysis.ProjectId == projectId
                && analysis.ConversationId == targetConversationId, cancellationToken);
        if (target is null)
        {
            source.ConversationId = targetConversationId;
            source.CustomerId = targetCustomerId;
            return;
        }
        if (source.AnalyzedAtUtc > target.AnalyzedAtUtc)
        {
            dbContext.ConversationSalesAnalyses.Remove(target);
            source.ConversationId = targetConversationId;
            source.CustomerId = targetCustomerId;
        }
        else
        {
            dbContext.ConversationSalesAnalyses.Remove(source);
        }
    }

    private async Task MergeMemoriesAsync(
        Guid projectId,
        Guid sourceCustomerId,
        Guid targetCustomerId,
        CancellationToken cancellationToken)
    {
        var target = await dbContext.CustomerMemories.IgnoreQueryFilters()
            .OrderByDescending(memory => memory.LastUpdatedAt)
            .FirstOrDefaultAsync(memory => memory.ProjectId == projectId
                && memory.CustomerId == targetCustomerId, cancellationToken);
        var sources = await dbContext.CustomerMemories.IgnoreQueryFilters()
            .Where(memory => memory.ProjectId == projectId
                && memory.CustomerId == sourceCustomerId)
            .OrderByDescending(memory => memory.LastUpdatedAt)
            .ToListAsync(cancellationToken);
        foreach (var source in sources)
        {
            if (target is null)
            {
                source.CustomerId = targetCustomerId;
                target = source;
                continue;
            }

            target.LongTermSummary = MergeText(target.LongTermSummary, source.LongTermSummary);
            target.FactsJson = PreferPopulatedJson(target.FactsJson, source.FactsJson);
            target.TriggersJson = PreferPopulatedJson(target.TriggersJson, source.TriggersJson);
            target.ObjectionsJson = PreferPopulatedJson(target.ObjectionsJson, source.ObjectionsJson);
            target.LastUpdatedAt = target.LastUpdatedAt > source.LastUpdatedAt
                ? target.LastUpdatedAt
                : source.LastUpdatedAt;
            dbContext.CustomerMemories.Remove(source);
        }
    }

    private async Task MergeCampaignRecipientsAsync(
        Guid projectId,
        Guid sourceCustomerId,
        Guid targetCustomerId,
        CancellationToken cancellationToken)
    {
        List<CampaignRecipient> customerRecipients;
        if (string.Equals(
                dbContext.Database.ProviderName,
                "Npgsql.EntityFrameworkCore.PostgreSQL",
                StringComparison.Ordinal)
            && dbContext.Database.CurrentTransaction is not null)
        {
            customerRecipients = await dbContext.CampaignRecipients
                .FromSqlInterpolated($"""
                    SELECT * FROM "CampaignRecipients"
                    WHERE "ProjectId" = {projectId}
                      AND ("CustomerId" = {sourceCustomerId} OR "CustomerId" = {targetCustomerId})
                    FOR UPDATE
                    """)
                .IgnoreQueryFilters()
                .ToListAsync(cancellationToken);
        }
        else
        {
            customerRecipients = await dbContext.CampaignRecipients.IgnoreQueryFilters()
                .Where(recipient => recipient.ProjectId == projectId
                    && (recipient.CustomerId == sourceCustomerId
                        || recipient.CustomerId == targetCustomerId))
                .ToListAsync(cancellationToken);
        }

        var sources = customerRecipients
            .Where(recipient => recipient.CustomerId == sourceCustomerId)
            .ToList();
        if (sources.Count == 0) return;
        var campaignIds = sources.Select(recipient => recipient.CampaignId).ToArray();
        var targets = customerRecipients
            .Where(recipient => recipient.CustomerId == targetCustomerId
                && campaignIds.Contains(recipient.CampaignId))
            .ToDictionary(recipient => recipient.CampaignId);
        foreach (var source in sources)
        {
            if (!targets.TryGetValue(source.CampaignId, out var target))
            {
                source.CustomerId = targetCustomerId;
                continue;
            }

            if (RecipientRank(source.Status) > RecipientRank(target.Status))
            {
                target.Status = source.Status;
                target.SentAt = source.SentAt;
                target.DeliveredAt = source.DeliveredAt;
                target.ReadAt = source.ReadAt;
                target.ErrorMessage = source.ErrorMessage;
                target.Variant = source.Variant;
            }
            dbContext.CampaignRecipients.Remove(source);
        }
    }

    private async Task MergeAdvertisingConsentAsync(
        Guid projectId,
        Guid sourceCustomerId,
        Guid targetCustomerId,
        CancellationToken cancellationToken)
    {
        var source = await dbContext.CustomerAdvertisingConsentProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId
                && item.CustomerId == sourceCustomerId, cancellationToken);
        if (source is null) return;
        var target = await dbContext.CustomerAdvertisingConsentProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(item => item.ProjectId == projectId
                && item.CustomerId == targetCustomerId, cancellationToken);
        if (target is null)
        {
            source.CustomerId = targetCustomerId;
            return;
        }
        if (source.ConsentVersion > target.ConsentVersion)
        {
            target.ConsentVersion = source.ConsentVersion;
            target.ConsentState = source.ConsentState;
            target.LegalBasis = source.LegalBasis;
            target.EffectiveAtUtc = source.EffectiveAtUtc;
            target.UpdatedFromEventId = source.UpdatedFromEventId;
            target.IsTombstoned = source.IsTombstoned;
        }
        dbContext.CustomerAdvertisingConsentProjections.Remove(source);
    }

    private static void MergeCustomerProfile(Customer source, Customer target)
    {
        if ((string.IsNullOrWhiteSpace(target.Name)
                || target.Name.StartsWith("WA Customer", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(source.Name)) target.Name = source.Name;
        if (string.IsNullOrWhiteSpace(target.City)) target.City = source.City;
        target.LeadScore = Math.Max(target.LeadScore, source.LeadScore);
        target.PurchaseProbability = Math.Max(target.PurchaseProbability, source.PurchaseProbability);
        target.Tags = target.Tags.Concat(source.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        target.Interests = target.Interests.Concat(source.Interests).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        target.Notes = MergeText(target.Notes, source.Notes);
        target.Budget ??= source.Budget;
        target.Label ??= source.Label;
        target.FacebookPSID ??= source.FacebookPSID;
        target.FacebookName ??= source.FacebookName;
        target.AIInsights = MergeText(target.AIInsights, source.AIInsights);
        target.AutomationRules ??= source.AutomationRules;
        target.WhatsAppLid ??= source.WhatsAppLid;
        target.IsBlacklisted |= source.IsBlacklisted;
        target.UpdatedAt = DateTime.UtcNow;
    }

    private static string MergeText(string? target, string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return target ?? string.Empty;
        if (string.IsNullOrWhiteSpace(target)) return source;
        return target.Contains(source, StringComparison.Ordinal) ? target : $"{target}\n{source}";
    }

    private static string PreferPopulatedJson(string target, string source) =>
        string.IsNullOrWhiteSpace(target) || target is "[]" or "{}" ? source : target;

    private static int RecipientRank(RecipientStatus status) => status switch
    {
        RecipientStatus.DeliveryUnknown => 9,
        RecipientStatus.Responded => 8,
        RecipientStatus.Read => 7,
        RecipientStatus.Delivered => 6,
        RecipientStatus.Sent => 5,
        RecipientStatus.Accelerated => 4,
        RecipientStatus.Processing => 2,
        RecipientStatus.Failed => 1,
        _ => 0
    };

    private Task<Customer> CanonicalCustomerAsync(
        Guid projectId,
        Guid customerId,
        CancellationToken cancellationToken) => dbContext.Customers.IgnoreQueryFilters()
        .FirstAsync(customer => customer.ProjectId == projectId
            && customer.Id == customerId, cancellationToken);

    private async Task<List<Customer>> PhoneCustomersAsync(
        Guid projectId,
        string canonicalPhone,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Customers.IgnoreQueryFilters()
            .Where(customer => customer.ProjectId == projectId);
        List<Customer> customers;
        if (string.Equals(
            dbContext.Database.ProviderName,
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            StringComparison.Ordinal))
        {
            customers = await query
                .Where(customer => EF.Property<string>(customer,
                    Modules.GroupAppointments.Domain.GroupBookingPhoneFields.CustomerCanonical) == canonicalPhone)
                .ToListAsync(cancellationToken);
        }
        else
        {
            customers = (await query.ToListAsync(cancellationToken))
                .Where(customer => GroupBookingPhone.Normalize(customer.PhoneNumber) == canonicalPhone)
                .ToList();
        }
        return customers
            .OrderBy(customer => customer.CreatedAt)
            .ThenBy(customer => customer.Id)
            .ToList();
    }

    private static Guid DeterministicId(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
