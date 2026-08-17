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
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == request.ProjectId && x.Id == request.EnvelopeId, cancellationToken);
        var start = DateTime.UtcNow.Date; var end = start.AddDays(1);
        var ledger = await db.AdvertisingBudgetLedgers.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == request.ProjectId && x.EnvelopeId == request.EnvelopeId && x.PeriodStartUtc == start, cancellationToken);
        if (ledger is null)
        {
            var reserve = decimal.Round(envelope.DailyCap * envelope.SafetyReservePercent / 100m, 2, MidpointRounding.ToZero);
            ledger = new BudgetPeriodLedger { ProjectId = request.ProjectId, EnvelopeId = request.EnvelopeId, PeriodStartUtc = start, PeriodEndUtc = end,
                AuthorizedCap = envelope.DailyCap, SafetyReserve = reserve, UsableCap = envelope.DailyCap - reserve, Currency = envelope.Currency };
            db.AdvertisingBudgetLedgers.Add(ledger);
        }
        var available = ledger.UsableCap - ledger.CommittedAmount + ledger.ReleasedAmount;
        var requested = request.Items.Sum(item => item.Amount);
        if (requested > available) { await transaction.RollbackAsync(cancellationToken); return new(false, [], available, "ADS_USABLE_CAP_EXCEEDED"); }
        var allocations = request.Items.Select(item => new BudgetAllocation
        {
            ProjectId = request.ProjectId, LedgerId = ledger.Id, TargetId = item.TargetId, Purpose = item.Purpose,
            AllocatedAmount = item.Amount, StartsAtUtc = start, EndsAtUtc = end, DecisionId = item.DecisionId
        }).ToList();
        ledger.CommittedAmount += requested; ledger.Version++;
        db.AdvertisingBudgetAllocations.AddRange(allocations);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(true, allocations.Select(allocation => allocation.Id).ToArray(), available - requested, "ADS_RESERVED");
    }

    public async Task ReleaseAsync(AppDbContext db, Guid projectId, Guid allocationId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var allocation = await db.AdvertisingBudgetAllocations.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == allocationId, cancellationToken);
        if (allocation.State != "Active") return;
        var ledger = await db.AdvertisingBudgetLedgers.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.Id == allocation.LedgerId, cancellationToken);
        allocation.State = "Released"; ledger.ReleasedAmount += allocation.AllocatedAmount; ledger.Version++;
        await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }
}
