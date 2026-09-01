using Modules.Advertising.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record AllocationSlice(BudgetPurpose Purpose, decimal Amount);
public sealed record AllocationResult(decimal Cap, decimal Reserve, decimal Usable, IReadOnlyList<AllocationSlice> Slices);
public sealed record BudgetReservationResult(bool Reserved, Guid? AllocationId, decimal Available, string Code);
public sealed record BudgetReservationItem(Guid TargetId, BudgetPurpose Purpose, decimal Amount, Guid? DecisionId = null);
public sealed record BudgetReservationBatch(Guid ProjectId, Guid EnvelopeId, IReadOnlyList<BudgetReservationItem> Items);
public sealed record BudgetBatchReservationResult(bool Reserved, IReadOnlyList<Guid> AllocationIds, decimal Available, string Code);

public sealed class BudgetAllocator
{
    public async Task<List<BudgetPeriodLedger>> EnsureCurrentLedgersAsync(AppDbContext db, AutonomyEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        var ledgers = new List<BudgetPeriodLedger>();
        foreach (var period in BudgetPeriodPolicy.Resolve(envelope, DateTime.UtcNow))
        {
            var ledger = await db.AdvertisingBudgetLedgers.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
                item.ProjectId == envelope.ProjectId && item.EnvelopeId == envelope.Id
                && item.PeriodKind == period.Kind && item.PeriodStartUtc == period.StartUtc, cancellationToken);
            if (ledger is null)
            {
                var reserve = decimal.Round(period.Cap * envelope.SafetyReservePercent / 100m, 2, MidpointRounding.ToZero);
                ledger = new BudgetPeriodLedger
                {
                    ProjectId = envelope.ProjectId, EnvelopeId = envelope.Id, EnvelopeVersion = envelope.Version,
                    PeriodKind = period.Kind, PeriodStartUtc = period.StartUtc, PeriodEndUtc = period.EndUtc,
                    AuthorizedCap = period.Cap, SafetyReserve = reserve, UsableCap = period.Cap - reserve,
                    Currency = envelope.Currency
                };
                db.AdvertisingBudgetLedgers.Add(ledger);
            }
            ledgers.Add(ledger);
        }
        return ledgers;
    }

    public AllocationResult Allocate(decimal dailyCap, decimal reservePercent, int creativeTests, bool hasRetargeting)
    {
        if (dailyCap <= 0) throw new ArgumentOutOfRangeException(nameof(dailyCap));
        if (reservePercent is < 0 or > 50) throw new ArgumentOutOfRangeException(nameof(reservePercent));

        var reserve = decimal.Round(dailyCap * reservePercent / 100m, 2, MidpointRounding.ToZero);
        var usable = dailyCap - reserve;
        var retargeting = hasRetargeting ? decimal.Round(usable * .05m, 2, MidpointRounding.ToZero) : 0m;
        var testPool = creativeTests > 0 ? decimal.Round(usable * .15m, 2, MidpointRounding.ToZero) : 0m;
        var audience = decimal.Round(usable * .10m, 2, MidpointRounding.ToZero);
        var winners = usable - retargeting - testPool - audience;

        var slices = new List<AllocationSlice> { new(BudgetPurpose.Winner, winners) };
        if (testPool > 0) slices.Add(new(BudgetPurpose.CreativeTest, testPool));
        slices.Add(new(BudgetPurpose.AudienceTest, audience));
        if (retargeting > 0) slices.Add(new(BudgetPurpose.Retargeting, retargeting));

        if (slices.Sum(x => x.Amount) > usable) throw new InvalidOperationException("Allocation exceeds usable cap.");
        return new(dailyCap, reserve, usable, slices);
    }

    public async Task<BudgetReservationResult> ReserveAsync(AppDbContext db, Guid projectId, Guid envelopeId, Guid targetId,
        BudgetPurpose purpose, decimal amount, Guid? decisionId = null, CancellationToken cancellationToken = default)
    {
        var batch = new BudgetReservationBatch(projectId, envelopeId, [new(targetId, purpose, amount, decisionId)]);
        var reservation = await ReserveBatchAsync(db, batch, cancellationToken);
        return new(reservation.Reserved, reservation.AllocationIds.FirstOrDefault(), reservation.Available, reservation.Code);
    }

    public async Task<BudgetBatchReservationResult> ReserveBatchAsync(AppDbContext db, BudgetReservationBatch request, CancellationToken cancellationToken = default)
    {
        if (request.Items.Count == 0 || request.Items.Any(item => item.Amount <= 0))
            return new(false, [], 0m, "ADS_INVALID_RESERVATION");
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken)
            : null;
        if (db.Database.IsNpgsql())
        {
            var lockKey = $"advertising-budget:{request.ProjectId:N}:{request.EnvelopeId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
        }
        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == request.ProjectId && x.Id == request.EnvelopeId, cancellationToken);
        if (envelope.State != EnvelopeState.Active)
            return new(false, [], 0m, "ADS_ENVELOPE_NOT_ACTIVE");
        var ledgers = await EnsureCurrentLedgersAsync(db, envelope, cancellationToken);
        var available = ledgers.Min(AdvertisingSpendGuard.RemainingAuthority);
        var requested = request.Items.Sum(item => item.Amount);
        if (requested > available)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return new(false, [], Math.Max(0m, available), "ADS_USABLE_CAP_EXCEEDED");
        }
        var daily = ledgers.Single(ledger => ledger.PeriodKind == "Daily");
        var allocations = request.Items.Select(item => new BudgetAllocation
        {
            ProjectId = request.ProjectId, LedgerId = daily.Id, TargetId = item.TargetId, Purpose = item.Purpose,
            AllocatedAmount = item.Amount, StartsAtUtc = daily.PeriodStartUtc, EndsAtUtc = daily.PeriodEndUtc, DecisionId = item.DecisionId
        }).ToList();
        db.AdvertisingBudgetAllocations.AddRange(allocations);
        foreach (var ledger in ledgers)
        {
            ledger.CommittedAmount += requested;
            ledger.Version++;
            db.AdvertisingBudgetAllocationDebits.AddRange(allocations.Select(allocation => new BudgetAllocationLedgerDebit
            {
                ProjectId = request.ProjectId, AllocationId = allocation.Id, LedgerId = ledger.Id, ReservedAmount = allocation.AllocatedAmount
            }));
        }
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return new(true, allocations.Select(allocation => allocation.Id).ToArray(), available - requested, "ADS_RESERVED");
    }

    public async Task ReleaseAsync(AppDbContext db, Guid projectId, Guid allocationId, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(db.Database.IsNpgsql() ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable, cancellationToken)
            : null;
        var allocation = await db.AdvertisingBudgetAllocations.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == allocationId, cancellationToken);
        if (db.Database.IsNpgsql())
        {
            var envelopeId = await db.AdvertisingBudgetLedgers.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.Id == allocation.LedgerId)
                .Select(x => x.EnvelopeId).SingleAsync(cancellationToken);
            var lockKey = $"advertising-budget:{projectId:N}:{envelopeId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({lockKey}))", cancellationToken);
            await db.Entry(allocation).ReloadAsync(cancellationToken);
        }
        if (allocation.State != "Active") return;
        var debits = await db.AdvertisingBudgetAllocationDebits.IgnoreQueryFilters()
            .Where(x => x.ProjectId == projectId && x.AllocationId == allocationId && x.State == "Reserved").ToListAsync(cancellationToken);
        var ledgerIds = debits.Select(debit => debit.LedgerId).ToArray();
        var ledgers = await db.AdvertisingBudgetLedgers.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && ledgerIds.Contains(x.Id)).ToListAsync(cancellationToken);
        allocation.State = "Released";
        foreach (var debit in debits)
        {
            debit.State = "Released"; debit.ReleasedAtUtc = DateTime.UtcNow;
            var ledger = ledgers.Single(item => item.Id == debit.LedgerId);
            ledger.ReleasedAmount += debit.ReservedAmount; ledger.Version++;
        }
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}

public sealed record BudgetPeriodDefinition(string Kind, DateTime StartUtc, DateTime EndUtc, decimal Cap);

public static class BudgetPeriodPolicy
{
    public static IReadOnlyList<BudgetPeriodDefinition> Resolve(AutonomyEnvelope envelope, DateTime nowUtc)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(envelope.ReportingTimezoneIana);
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), timezone);
        var dayStartLocal = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
        var dayEndLocal = dayStartLocal.AddDays(1);
        var result = new List<BudgetPeriodDefinition>
        {
            new("Daily", TimeZoneInfo.ConvertTimeToUtc(dayStartLocal, timezone),
                TimeZoneInfo.ConvertTimeToUtc(dayEndLocal, timezone), envelope.DailyCap)
        };
        if (envelope.PeriodCap is { } periodCap)
        {
            if (string.Equals(envelope.PeriodCapKind, "Total", StringComparison.OrdinalIgnoreCase))
                result.Add(new("Total", envelope.StartsAtUtc, envelope.EndsAtUtc ?? DateTime.SpecifyKind(new DateTime(9998, 12, 31), DateTimeKind.Utc), periodCap));
            else
            {
                var monthStart = new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
                result.Add(new("Monthly", TimeZoneInfo.ConvertTimeToUtc(monthStart, timezone),
                    TimeZoneInfo.ConvertTimeToUtc(monthStart.AddMonths(1), timezone), periodCap));
            }
        }
        return result;
    }
}
