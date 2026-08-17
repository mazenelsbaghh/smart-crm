using Modules.AI.Services;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class DecisionPipelineTests
{
    [Fact]
    public async Task Generic_financial_action_requires_independent_auditor_approval()
    {
        var ai = new AdvertisingDecisionAi(new Stub("{\"action\":\"IncreaseBudget\",\"confidence\":0.9,\"reasons\":[\"winner\"]}", "{\"verdict\":\"APPROVE\",\"reasons\":[\"cap_ok\"]}"), new ProjectAiStub());
        var result = await ai.ReviewActionAsync(Guid.NewGuid(), "IncreaseBudget", "{\"roas\":2.4}", CancellationToken.None);
        Assert.Equal(DecisionVerdict.Approve, result.StrategistVerdict); Assert.Equal(DecisionVerdict.Approve, result.AuditorVerdict);
    }

    [Fact]
    public async Task Invalid_auditor_schema_waits_instead_of_spending()
    {
        var ai = new AdvertisingDecisionAi(new Stub("{\"action\":\"PauseAd\",\"confidence\":0.8,\"reasons\":[]}", "bad"), new ProjectAiStub());
        Assert.Equal(DecisionVerdict.Wait, (await ai.ReviewActionAsync(Guid.NewGuid(), "PauseAd", "{}", CancellationToken.None)).AuditorVerdict);
    }

    private sealed class Stub(params string[] output) : IGeminiClient
    {
        private readonly Queue<string> _output = new(output);
        public Task<string> GenerateReplyAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => Task.FromResult(_output.Dequeue());
        public Task<string> GenerateReplyAsync(string messageContent, byte[] fileBytes, string mimeType, string apiKeyOverride = null!, string modelOverride = null!, string cachedContentId = null!) => throw new NotSupportedException();
        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) => throw new NotSupportedException();
        public Task<int> CountTokensAsync(string messageContent, string apiKeyOverride = null!, string modelOverride = null!) => throw new NotSupportedException();
        public Task<string> CreateContextCacheAsync(string staticContent, string model, int ttlSeconds, string apiKeyOverride = null!) => throw new NotSupportedException();
    }

    private sealed class ProjectAiStub : IProjectAiConfigurationProvider
    {
        public Task<ProjectAiConfiguration> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProjectAiConfiguration(null, null));
    }
}
