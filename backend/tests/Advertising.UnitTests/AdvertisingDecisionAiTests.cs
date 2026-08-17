using Modules.AI.Services;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingDecisionAiTests
{
    [Fact]
    public async Task Independent_approval_returns_approved_canary_review()
    {
        var model = new ModelBoundaryStub(
            "{\"action\":\"ActivateCanary\",\"confidence\":0.8,\"reasons\":[\"envelope_ready\"]}",
            "{\"verdict\":\"APPROVE\",\"reasons\":[\"evidence_sufficient\"]}");

        var review = await new AdvertisingDecisionAi(model, new ProjectAiStub()).ReviewCanaryAsync(Guid.NewGuid(), "{\"tracking\":\"healthy\"}", CancellationToken.None);

        Assert.Equal(DecisionVerdict.Approve, review.StrategistVerdict);
        Assert.Equal(DecisionVerdict.Approve, review.AuditorVerdict);
        Assert.All(model.Calls, call => Assert.Equal(("project-api-key", "gemini-3.1-flash-lite"), call));
    }

    [Theory]
    [InlineData("not json", "{}")]
    [InlineData("{\"action\":\"Wait\",\"confidence\":0.2,\"reasons\":[]}", "{}")]
    public async Task Invalid_or_waiting_strategy_fails_closed(string strategistResponse, string auditorResponse)
    {
        var review = await new AdvertisingDecisionAi(new ModelBoundaryStub(strategistResponse, auditorResponse), new ProjectAiStub())
            .ReviewCanaryAsync(Guid.NewGuid(), "{}", CancellationToken.None);

        Assert.Equal(DecisionVerdict.Wait, review.StrategistVerdict);
        Assert.Equal(DecisionVerdict.Wait, review.AuditorVerdict);
    }

    private sealed class ModelBoundaryStub(params string[] responses) : IGeminiClient
    {
        private readonly Queue<string> _responses = new(responses);
        public List<(string? ApiKey, string? Model)> Calls { get; } = [];
        public Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!)
        {
            Calls.Add((apiKeyOverride, modelOverride));
            return Task.FromResult(_responses.Dequeue());
        }
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string apiKeyOverride = null!) => throw new NotSupportedException();
    }

    private sealed class ProjectAiStub : IProjectAiConfigurationProvider
    {
        public Task<ProjectAiConfiguration> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProjectAiConfiguration("project-api-key", "gemini-3.1-flash-lite"));
    }
}
