using Modules.CRM.Domain;

namespace Modules.CRM.Services;

public static class PlannedFollowUpTiming
{
    public static bool DeferUntilPredecessor(FollowUp followUp, FollowUp? predecessor, DateTime nowUtc)
    {
        if (!followUp.DependsOnFollowUpId.HasValue) return false;
        if (predecessor is null || predecessor.Status == "DeliveryUnknown")
        {
            followUp.Status = predecessor is null ? "Cancelled" : "DeliveryUnknown";
            return true;
        }
        var terminal = predecessor.Status is "Completed" or "Cancelled" or "Missed" or "Bypassed";
        var earliest = terminal
            ? (predecessor.SentAtUtc ?? predecessor.UpdatedAt).AddSeconds(followUp.DispatchIntervalSeconds!.Value)
            : new[] { nowUtc.AddSeconds(5), predecessor.DueDate.AddSeconds(followUp.DispatchIntervalSeconds!.Value) }.Max();
        if (earliest > followUp.DueDate) followUp.DueDate = earliest;
        if (followUp.DueDate <= nowUtc && terminal) return false;
        followUp.Status = "Pending";
        followUp.UpdatedAt = nowUtc;
        return true;
    }
}
