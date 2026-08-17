using System.Text.Json;
using Modules.AI.Services;
using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public sealed record AiActivationReview(DecisionVerdict StrategistVerdict, DecisionVerdict AuditorVerdict, string StrategistJson, string AuditorJson, string Reason);
internal sealed record StrategistResponse(string Action, decimal Confidence, string[] Reasons);
internal sealed record AuditorResponse(string Verdict, string[] Reasons);

public sealed class AdvertisingDecisionAi(IGeminiClient gemini, IProjectAiConfigurationProvider projectAi)
{
    public async Task<AiActivationReview> ReviewCanaryAsync(Guid projectId, string evidenceJson, CancellationToken cancellationToken = default)
    {
        var aiConfiguration = await projectAi.GetAsync(projectId, cancellationToken);
        var strategistPrompt = "You are an advertising strategist. Evaluate only the server evidence below. Return JSON only: {\"action\":\"ActivateCanary|Wait\",\"confidence\":0.0,\"reasons\":[\"reason code\"]}. Never change project, platform, offer or budget. Evidence: " + evidenceJson;
        var strategistRaw = await gemini.GenerateReplyAsync(strategistPrompt, aiConfiguration.ApiKey, aiConfiguration.Model);
        if (!TryDeserialize<StrategistResponse>(strategistRaw, out var strategist) || strategist.Action != "ActivateCanary")
            return new(DecisionVerdict.Wait, DecisionVerdict.Wait, strategistRaw, "{}", "Strategist did not return a valid canary activation proposal.");

        var auditorPrompt = "You are an independent advertising auditor. You did not participate in the proposal. Review the raw server evidence and proposal. Return JSON only: {\"verdict\":\"APPROVE|REJECT|WAIT|ESCALATE\",\"reasons\":[\"reason code\"]}. Evidence: " + evidenceJson + " Proposal: " + JsonSerializer.Serialize(strategist);
        var auditorRaw = await gemini.GenerateReplyAsync(auditorPrompt, aiConfiguration.ApiKey, aiConfiguration.Model);
        if (!TryDeserialize<AuditorResponse>(auditorRaw, out var auditor))
            return new(DecisionVerdict.Approve, DecisionVerdict.Wait, strategistRaw, auditorRaw, "Auditor response failed schema validation.");
        var verdict = auditor.Verdict.ToUpperInvariant() switch { "APPROVE" => DecisionVerdict.Approve, "REJECT" => DecisionVerdict.Reject, "ESCALATE" => DecisionVerdict.Escalate, _ => DecisionVerdict.Wait };
        return new(DecisionVerdict.Approve, verdict, JsonSerializer.Serialize(strategist), JsonSerializer.Serialize(auditor), string.Join(",", auditor.Reasons));
    }

    public async Task<AiActivationReview> ReviewActionAsync(Guid projectId, string action, string evidenceJson, CancellationToken cancellationToken = default)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "IncreaseBudget", "DecreaseBudget", "PauseAd", "ResumeAd", "CreateTest", "Retargeting", "Wait" };
        if (!allowed.Contains(action)) return new(DecisionVerdict.Reject, DecisionVerdict.Reject, "{}", "{}", "Unsupported action.");
        var aiConfiguration = await projectAi.GetAsync(projectId, cancellationToken);
        var strategistRaw = await gemini.GenerateReplyAsync($"Act as the advertising strategist. Evidence is server-computed. Return JSON only: {{\"action\":\"{action}|Wait\",\"confidence\":0.0,\"reasons\":[\"code\"]}}. Evidence: {evidenceJson}", aiConfiguration.ApiKey, aiConfiguration.Model);
        if (!TryDeserialize<StrategistResponse>(strategistRaw, out var strategist) || !string.Equals(strategist.Action, action, StringComparison.OrdinalIgnoreCase))
            return new(DecisionVerdict.Wait, DecisionVerdict.Wait, strategistRaw, "{}", "Strategist returned WAIT or invalid schema.");
        var auditorRaw = await gemini.GenerateReplyAsync("Act as an independent advertising auditor. Return JSON only: {\"verdict\":\"APPROVE|REJECT|WAIT|ESCALATE\",\"reasons\":[\"code\"]}. Evidence: " + evidenceJson + " Proposal: " + JsonSerializer.Serialize(strategist), aiConfiguration.ApiKey, aiConfiguration.Model);
        if (!TryDeserialize<AuditorResponse>(auditorRaw, out var auditor))
            return new(DecisionVerdict.Approve, DecisionVerdict.Wait, strategistRaw, auditorRaw, "Auditor schema invalid.");
        var verdict = auditor.Verdict.ToUpperInvariant() switch { "APPROVE" => DecisionVerdict.Approve, "REJECT" => DecisionVerdict.Reject, "ESCALATE" => DecisionVerdict.Escalate, _ => DecisionVerdict.Wait };
        return new(DecisionVerdict.Approve, verdict, JsonSerializer.Serialize(strategist), JsonSerializer.Serialize(auditor), string.Join(',', auditor.Reasons));
    }

    private static bool TryDeserialize<T>(string raw, out T parsed) where T : class
    {
        parsed = null!;
        var json = raw.Trim();
        if (json.StartsWith("```")) json = json.Replace("```json", "", StringComparison.OrdinalIgnoreCase).Replace("```", "", StringComparison.Ordinal).Trim();
        try { parsed = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!; return parsed is not null; }
        catch (JsonException) { return false; }
    }
}
