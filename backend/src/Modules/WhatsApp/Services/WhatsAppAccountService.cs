using Microsoft.EntityFrameworkCore;
using Modules.WhatsApp.Domain;
using Shared.Infrastructure;

namespace Modules.WhatsApp.Services;

public sealed class WhatsAppAccountService(AppDbContext dbContext)
{
    public static Guid LegacyAccountId(Guid projectId) => projectId;

    public static Guid? GatewayAccountId(Guid projectId, Guid accountId) =>
        accountId == LegacyAccountId(projectId) ? null : accountId;

    public async Task<WhatsAppAccount> GetDefaultAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.WhatsAppAccounts
            .IgnoreQueryFilters()
            .Where(account => account.ProjectId == projectId)
            .OrderByDescending(account => account.IsDefault)
            .ThenBy(account => account.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsDefault)
            {
                existing.IsDefault = true;
                existing.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            return existing;
        }

        var legacy = new WhatsAppAccount
        {
            Id = LegacyAccountId(projectId),
            ProjectId = projectId,
            Name = "واتساب الرئيسي",
            IsDefault = true
        };
        dbContext.WhatsAppAccounts.Add(legacy);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return legacy;
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(legacy).State = EntityState.Detached;
            var concurrentDefault = await dbContext.WhatsAppAccounts
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    account => account.ProjectId == projectId && account.IsDefault,
                    cancellationToken);
            if (concurrentDefault is not null) return concurrentDefault;
            throw;
        }
    }

    public async Task<WhatsAppAccount?> ResolveAsync(
        Guid projectId,
        Guid? accountId,
        CancellationToken cancellationToken = default)
    {
        if (!accountId.HasValue)
            return await GetDefaultAsync(projectId, cancellationToken);

        var account = await dbContext.WhatsAppAccounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                account => account.ProjectId == projectId && account.Id == accountId.Value,
                cancellationToken);
        if (account is not null || accountId.Value != LegacyAccountId(projectId)) return account;

        // A restored legacy gateway session may emit its canonical project/account id
        // before this project has ever opened the account-management screen.
        await GetDefaultAsync(projectId, cancellationToken);
        return await dbContext.WhatsAppAccounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                candidate => candidate.ProjectId == projectId && candidate.Id == accountId.Value,
                cancellationToken);
    }

    public async Task<IReadOnlyList<WhatsAppAccount>> ListAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        await GetDefaultAsync(projectId, cancellationToken);
        return await dbContext.WhatsAppAccounts
            .IgnoreQueryFilters()
            .Where(account => account.ProjectId == projectId)
            .OrderByDescending(account => account.IsDefault)
            .ThenBy(account => account.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<WhatsAppAccount> CreateAsync(
        Guid projectId,
        string name,
        CancellationToken cancellationToken = default)
    {
        await GetDefaultAsync(projectId, cancellationToken);
        var account = new WhatsAppAccount
        {
            ProjectId = projectId,
            Name = NormalizeName(name),
            IsDefault = false
        };
        dbContext.WhatsAppAccounts.Add(account);
        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<WhatsAppAccount?> UpdateAsync(
        Guid projectId,
        Guid accountId,
        string name,
        bool makeDefault,
        CancellationToken cancellationToken = default)
    {
        var account = await ResolveAsync(projectId, accountId, cancellationToken);
        if (account is null) return null;

        account.Name = NormalizeName(name);
        account.UpdatedAt = DateTime.UtcNow;
        if (makeDefault && !account.IsDefault)
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
            var currentDefaults = await dbContext.WhatsAppAccounts
                .IgnoreQueryFilters()
                .Where(candidate => candidate.ProjectId == projectId && candidate.IsDefault)
                .ToListAsync(cancellationToken);
            foreach (var currentDefault in currentDefaults)
            {
                currentDefault.IsDefault = false;
                currentDefault.UpdatedAt = DateTime.UtcNow;
            }
            // Persist the old default first so the filtered unique index is never
            // transiently violated when EF orders the UPDATE statements.
            await dbContext.SaveChangesAsync(cancellationToken);
            account.IsDefault = true;
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return account;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return account;
    }

    private static string NormalizeName(string name)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0) return "رقم واتساب";
        return normalized.Length <= 80 ? normalized : normalized[..80];
    }
}
