using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using StackExchange.Redis;

namespace Modules.AI.Services;

public sealed class AiGenerationCache(
    IConnectionMultiplexer redis,
    IDataProtectionProvider protection,
    ILogger<AiGenerationCache> logger)
{
    private readonly IDataProtector protector = protection.CreateProtector("AI.GenerationCache.v1");
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(5);

    public async Task<string> GetOrCreateAsync(string key, Func<Task<string>> generate, TimeSpan lifetime)
    {
        var database = redis.GetDatabase();
        var owner = Guid.NewGuid().ToString("N");
        var existing = await WaitForLeaseAsync(database, key, owner);
        if (existing is not null) return existing;
        try
        {
            var cached = await ReadAsync(database, key);
            if (cached is not null) return cached;
            var generated = await generate();
            await StoreAsync(database, key, generated, lifetime);
            return generated;
        }
        finally
        {
            try { await database.LockReleaseAsync(key + ":lock", owner); }
            catch (RedisException) { logger.LogWarning("AI generation cache lease will expire automatically."); }
        }
    }

    public async Task<string> GetOrGenerateReplyAsync(string key, Func<Task<string>> generate, TimeSpan lifetime)
    {
        try { return await GetOrCreateAsync(key, generate, lifetime); }
        catch (RedisException)
        {
            logger.LogWarning("AI reply cache unavailable; generating a reply without cache.");
            return await generate();
        }
    }

    private async Task<string?> WaitForLeaseAsync(IDatabase database, string key, string owner)
    {
        for (var attempt = 0; attempt < 1500; attempt++)
        {
            var cached = await ReadAsync(database, key);
            if (cached is not null) return cached;
            if (await database.LockTakeAsync(key + ":lock", owner, LeaseDuration)) return null;
            await Task.Delay(200);
        }
        throw new TimeoutException("Another worker is still generating this AI result.");
    }

    private async Task<string?> ReadAsync(IDatabase database, string key)
    {
        try
        {
            var cached = await database.StringGetAsync(key);
            return cached.IsNullOrEmpty ? null : protector.Unprotect(cached.ToString());
        }
        catch (RedisException)
        {
            logger.LogWarning("AI cache read unavailable; continuing with generation if the lease can be acquired.");
            return null;
        }
        catch (CryptographicException)
        {
            logger.LogWarning("An unreadable AI cache entry was ignored.");
            return null;
        }
    }

    private async Task StoreAsync(IDatabase database, string key, string generated, TimeSpan lifetime)
    {
        try { await database.StringSetAsync(key, protector.Protect(generated), lifetime); }
        catch (RedisException) { logger.LogWarning("AI result could not be cached; returning the generated result."); }
    }
}
