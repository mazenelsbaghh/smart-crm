using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using StackExchange.Redis;
using Xunit;

namespace Advertising.IntegrationTests;

public sealed class AiGenerationCacheTests : IAsyncLifetime
{
    private readonly IContainer container = new ContainerBuilder("redis:7-alpine")
        .WithPortBinding(6379, true).WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build();
    private IConnectionMultiplexer redis = null!;
    private readonly IDataProtectionProvider protection = new EphemeralDataProtectionProvider();

    public async Task InitializeAsync()
    {
        await container.StartAsync();
        var options = ConfigurationOptions.Parse($"{container.Hostname}:{container.GetMappedPublicPort(6379)}");
        options.AsyncTimeout = 1000;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        redis = await ConnectionMultiplexer.ConnectAsync(options);
    }

    public async Task DisposeAsync()
    {
        redis.Dispose();
        await container.DisposeAsync();
    }

    [Fact]
    public async Task Concurrent_workers_and_redelivery_reuse_one_protected_generation()
    {
        var key = "ai:reply:" + Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var generations = 0;
        async Task<string> Generate()
        {
            Interlocked.Increment(ref generations);
            entered.TrySetResult();
            await release.Task;
            return "private customer reply";
        }
        var first = Cache().GetOrCreateAsync(key, Generate, TimeSpan.FromMinutes(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Cache().GetOrCreateAsync(key, Generate, TimeSpan.FromMinutes(1));
        release.SetResult();
        var replies = await Task.WhenAll(first, second);
        var redelivery = await Cache().GetOrCreateAsync(key, Generate, TimeSpan.FromMinutes(1));
        Assert.All(replies, reply => Assert.Equal(redelivery, reply));
        Assert.Equal("private customer reply", redelivery);
        Assert.Equal(1, generations);
        Assert.DoesNotContain("private customer reply", (await redis.GetDatabase().StringGetAsync(key)).ToString());
    }

    [Fact]
    public async Task Failed_generation_releases_the_lease_and_other_turns_remain_independent()
    {
        var key = "ai:reply:" + Guid.NewGuid();
        await Assert.ThrowsAsync<HttpRequestException>(() => Cache().GetOrCreateAsync(key,
            () => throw new HttpRequestException("provider failed"), TimeSpan.FromMinutes(1)));
        Assert.False(await redis.GetDatabase().KeyExistsAsync(key + ":lock"));
        Assert.Equal("recovered", await Cache().GetOrCreateAsync(key,
            () => Task.FromResult("recovered"), TimeSpan.FromMinutes(1)));
        Assert.Equal("other customer", await Cache().GetOrCreateAsync(key + ":other",
            () => Task.FromResult("other customer"), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public async Task Redis_outage_still_allows_a_reply_but_does_not_create_unusable_provider_context_caches()
    {
        await container.StopAsync();
        var generated = 0;
        Task<string> Generate()
        {
            generated++;
            return Task.FromResult("answer");
        }
        Assert.Equal("answer", await Cache().GetOrGenerateReplyAsync("reply", Generate, TimeSpan.FromMinutes(1)));
        Assert.Equal(1, generated);
        await Assert.ThrowsAnyAsync<RedisException>(() =>
            Cache().GetOrCreateAsync("context", Generate, TimeSpan.FromMinutes(1)));
        Assert.Equal(1, generated);
    }

    private AiGenerationCache Cache() => new(redis, protection, NullLogger<AiGenerationCache>.Instance);
}
