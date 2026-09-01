using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Modules.Advertising.Workers;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingAiWorkTests
{
    [Fact]
    public void Current_pending_completion_with_matching_owner_version_and_hash_is_accepted()
    {
        var ownerId = Guid.NewGuid();
        var work = Work(ownerId);
        var completion = Completion(work, ownerId);

        var result = AdvertisingAiWorkResultGuard.Evaluate(work, completion, DateTime.UtcNow);

        Assert.Equal(AiWorkCompletionDecision.Accept, result);
    }

    [Fact]
    public void Late_or_stale_completion_is_rejected()
    {
        var ownerId = Guid.NewGuid();
        var work = Work(ownerId);

        Assert.Equal(
            AiWorkCompletionDecision.RejectOwner,
            AdvertisingAiWorkResultGuard.Evaluate(work, Completion(work, Guid.NewGuid()), DateTime.UtcNow));
        Assert.Equal(
            AiWorkCompletionDecision.RejectHash,
            AdvertisingAiWorkResultGuard.Evaluate(work, Completion(work, ownerId) with { InputHash = "other" }, DateTime.UtcNow));
        Assert.Equal(
            AiWorkCompletionDecision.RejectExpired,
            AdvertisingAiWorkResultGuard.Evaluate(work, Completion(work, ownerId), work.DeadlineUtc.AddSeconds(1)));
    }

    [Fact]
    public void Advertising_ai_work_contract_never_contains_a_credential()
    {
        var request = AdvertisingAiWorkRequestContract.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Strategist",
            "input-hash",
            "{\"evidence\":true}");

        var serialized = System.Text.Json.JsonSerializer.Serialize(request);
        Assert.DoesNotContain("apiKey", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Review_result_rejects_provider_identifiers_or_unknown_fields_from_the_model()
    {
        Assert.True(AdvertisingAiResultSchema.IsValid("Strategist", "{\"verdict\":\"WAIT\",\"reasons\":[\"ADS_WAIT_VOLUME\"]}"));
        Assert.False(AdvertisingAiResultSchema.IsValid("Strategist", "{\"verdict\":\"APPROVE\",\"reasons\":[],\"adId\":\"invented\"}"));
        Assert.False(AdvertisingAiResultSchema.IsValid("Judge", "{\"verdict\":\"DO_IT\",\"reasons\":[]}"));
    }

    private static AdvertisingAiWorkItemSnapshot Work(Guid ownerId) => new(
        Id: Guid.NewGuid(),
        ProjectId: Guid.NewGuid(),
        OwnerId: ownerId,
        OwnerVersion: 3,
        InputHash: "input-hash",
        State: AiWorkState.Pending,
        DeadlineUtc: DateTime.UtcNow.AddMinutes(5));

    private static AdvertisingAiWorkCompletion Completion(AdvertisingAiWorkItemSnapshot work, Guid ownerId) => new(
        WorkItemId: work.Id,
        ProjectId: work.ProjectId,
        OwnerId: ownerId,
        OwnerVersion: work.OwnerVersion,
        InputHash: work.InputHash,
        ResultJson: "{}");
}
