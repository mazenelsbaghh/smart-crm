using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record ExperimentArmDefinition(string Name, bool IsControl, Guid PlanId, string ChangedValueJson, decimal Budget);
public sealed record ExperimentDefinition(Guid OfferId, Guid DestinationId, Guid EnvelopeId, string Name, string Hypothesis,
    string PrimaryVariable, string BusinessOutcome, int AttributionWindowDays, int MinimumElapsedHours,
    decimal MinimumSpend, int MinimumAttributedOutcomes, decimal MinimumCoverage, int CorrectionLagHours,
    decimal BudgetCap, string StopRuleJson, IReadOnlyList<ExperimentArmDefinition> Arms);
public sealed record ExperimentArmEvidence(Guid ArmId, decimal Spend, int Outcomes, decimal Cpa, decimal PaidValue,
    decimal Coverage, bool HasPendingCorrection);
public sealed record ExperimentDecision(string Verdict, Guid? WinnerArmId, IReadOnlyList<string> Reasons);

public sealed class AdvertisingExperimentService(AppDbContext db)
{
    public async Task<AdvertisingExperiment> CreateAsync(Guid projectId, ExperimentDefinition definition,
        CancellationToken cancellationToken = default)
    {
        Validate(definition);
        var experiment = new AdvertisingExperiment
        {
            ProjectId = projectId, OfferId = definition.OfferId, DestinationId = definition.DestinationId,
            EnvelopeId = definition.EnvelopeId, Name = definition.Name, Hypothesis = definition.Hypothesis,
            PrimaryVariable = definition.PrimaryVariable, BusinessOutcome = definition.BusinessOutcome,
            AttributionWindowDays = definition.AttributionWindowDays, MinimumElapsedHours = definition.MinimumElapsedHours,
            MinimumSpend = definition.MinimumSpend, MinimumAttributedOutcomes = definition.MinimumAttributedOutcomes,
            MinimumAttributionCoverage = definition.MinimumCoverage, CorrectionLagHours = definition.CorrectionLagHours,
            BudgetCap = definition.BudgetCap, StopRuleJson = definition.StopRuleJson,
            DefinitionHash = AdvertisingAuditService.HashState(JsonSerializer.Serialize(definition)), State = "Planned"
        };
        db.AdvertisingExperiments.Add(experiment);
        db.AdvertisingExperimentArms.AddRange(definition.Arms.Select(arm => new AdvertisingExperimentArm
        {
            ProjectId = projectId, ExperimentId = experiment.Id, Name = arm.Name, IsControl = arm.IsControl,
            ChangedValueJson = arm.ChangedValueJson, PlanId = arm.PlanId, AllocatedBudget = arm.Budget, State = "Planned"
        }));
        await db.SaveChangesAsync(cancellationToken);
        return experiment;
    }

    public static ExperimentDecision Evaluate(AdvertisingExperiment experiment, IReadOnlyList<ExperimentArmEvidence> arms,
        DateTime nowUtc)
    {
        var reasons = new List<string>();
        if (experiment.StartedAtUtc is null) reasons.Add("ADS_WAIT_EXPERIMENT_NOT_STARTED");
        else if (nowUtc - experiment.StartedAtUtc < TimeSpan.FromHours(experiment.MinimumElapsedHours)) reasons.Add("ADS_WAIT_MINIMUM_TIME");
        if (arms.Sum(arm => arm.Spend) < experiment.MinimumSpend) reasons.Add("ADS_WAIT_MINIMUM_SPEND");
        if (arms.Sum(arm => arm.Outcomes) < experiment.MinimumAttributedOutcomes) reasons.Add("ADS_WAIT_MINIMUM_OUTCOMES");
        if (arms.Count == 0 || arms.Min(arm => arm.Coverage) < experiment.MinimumAttributionCoverage) reasons.Add("ADS_WAIT_ATTRIBUTION_COVERAGE");
        if (arms.Any(arm => arm.HasPendingCorrection)) reasons.Add("ADS_WAIT_PENDING_CORRECTION");
        if (experiment.StartedAtUtc is { } started && nowUtc < started.AddDays(experiment.AttributionWindowDays).AddHours(experiment.CorrectionLagHours))
            reasons.Add("ADS_WAIT_ATTRIBUTION_DELAY");
        if (reasons.Count > 0) return new("WAIT", null, reasons);
        var ranked = arms.Where(arm => arm.Outcomes > 0).OrderBy(arm => arm.Cpa).ThenByDescending(arm => arm.PaidValue).ThenBy(arm => arm.ArmId).ToArray();
        if (ranked.Length < 2) return new("INCONCLUSIVE", null, ["ADS_EXPERIMENT_NOT_COMPARABLE"]);
        var improvement = ranked[1].Cpa > 0 ? (ranked[1].Cpa - ranked[0].Cpa) / ranked[1].Cpa : 0m;
        return improvement >= .15m ? new("WINNER", ranked[0].ArmId, ["CPA_IMPROVEMENT_CONFIRMED"])
            : new("INCONCLUSIVE", null, ["ADS_EXPERIMENT_DIFFERENCE_TOO_SMALL"]);
    }

    private static void Validate(ExperimentDefinition definition)
    {
        if (definition.Arms.Count < 2 || definition.Arms.Count(arm => arm.IsControl) != 1)
            throw new AdvertisingException("ADS_EXPERIMENT_CONTROL_REQUIRED", "One control and at least one variant are required.", 422);
        foreach (var arm in definition.Arms.Where(arm => !arm.IsControl))
        {
            using var json = JsonDocument.Parse(arm.ChangedValueJson);
            var properties = json.RootElement.ValueKind == JsonValueKind.Object ? json.RootElement.EnumerateObject().ToArray() : [];
            if (properties.Length != 1 || !string.Equals(properties[0].Name, definition.PrimaryVariable, StringComparison.OrdinalIgnoreCase))
                throw new AdvertisingException("ADS_EXPERIMENT_MULTIPLE_VARIABLES", "Every variant must change exactly the declared variable.", 422);
        }
        if (definition.Arms.Sum(arm => arm.Budget) > definition.BudgetCap)
            throw new AdvertisingException("ADS_EXPERIMENT_BUDGET_EXCEEDED", "Experiment arms exceed the experiment cap.", 422);
    }
}
