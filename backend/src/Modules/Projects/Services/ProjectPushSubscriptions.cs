using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shared.Infrastructure;
using Shared.Security;
using StackExchange.Redis;

namespace Modules.Projects.Services;

public sealed record PushSubscription(Guid UserId, Guid ProjectId, Guid SessionId, string Role);

public sealed class ProjectPushSubscriptions(AppDbContext db, IDatabase redis)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static string RegistrationKey(string token) =>
        "fcm_registration:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public async Task RegisterAsync(ClaimsPrincipal user, string token)
    {
        if (!await new AccessSessionValidator(db).IsActiveAsync(user)) throw new UnauthorizedAccessException();
        var owner = new PushSubscription(Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!),
            Guid.Parse(user.FindFirstValue("ProjectId")!), Guid.Parse(user.FindFirstValue("sid")!),
            user.FindFirstValue(ClaimTypes.Role)!);
        const string register = """
            local previous = redis.call('get', KEYS[1])
            if previous then
                local ok, owner = pcall(cjson.decode, previous)
                if ok and owner.projectId then redis.call('srem', 'fcm_tokens:' .. owner.projectId, ARGV[2]) end
            end
            redis.call('set', KEYS[1], ARGV[1], 'EX', ARGV[3])
            redis.call('sadd', KEYS[2], ARGV[2])
            redis.call('expire', KEYS[2], ARGV[3])
            return 1
            """;
        await redis.ScriptEvaluateAsync(register,
            [new RedisKey(RegistrationKey(token)), new RedisKey($"fcm_tokens:{owner.ProjectId}")],
            [new RedisValue(JsonSerializer.Serialize(owner, Json)), new RedisValue(token), (RedisValue)(30 * 24 * 60 * 60)]);
    }

    public async Task<bool> RemoveAsync(ClaimsPrincipal user, string token)
    {
        var owner = await OwnerAsync(token);
        if (owner is null) return true;
        if (owner.UserId.ToString() != user.FindFirstValue(ClaimTypes.NameIdentifier)
            || owner.ProjectId.ToString() != user.FindFirstValue("ProjectId")
            || owner.SessionId.ToString() != user.FindFirstValue("sid")) return false;
        const string remove = """
            if redis.call('get', KEYS[1]) ~= ARGV[1] then return 0 end
            redis.call('srem', KEYS[2], ARGV[2])
            redis.call('del', KEYS[1])
            return 1
            """;
        await redis.ScriptEvaluateAsync(remove,
            [new RedisKey(RegistrationKey(token)), new RedisKey($"fcm_tokens:{owner.ProjectId}")],
            [new RedisValue(JsonSerializer.Serialize(owner, Json)), new RedisValue(token)]);
        return true;
    }

    public async Task<List<string>> ActiveTokensAsync(Guid projectId)
    {
        var active = new List<string>();
        foreach (var tokenValue in await redis.SetMembersAsync($"fcm_tokens:{projectId}"))
        {
            var token = tokenValue.ToString();
            var owner = await OwnerAsync(token);
            if (owner?.ProjectId == projectId && await new AccessSessionValidator(db).IsActiveAsync(Principal(owner)))
                active.Add(token);
            else
                await redis.SetRemoveAsync($"fcm_tokens:{projectId}", token);
        }
        return active;
    }

    private async Task<PushSubscription?> OwnerAsync(string token)
    {
        var stored = await redis.StringGetAsync(RegistrationKey(token));
        if (stored.IsNullOrEmpty) return null;
        try { return JsonSerializer.Deserialize<PushSubscription>(stored.ToString(), Json); }
        catch (JsonException) { return null; }
    }

    private static ClaimsPrincipal Principal(PushSubscription owner) => new(new ClaimsIdentity([
        new Claim(ClaimTypes.NameIdentifier, owner.UserId.ToString()), new Claim("ProjectId", owner.ProjectId.ToString()),
        new Claim("sid", owner.SessionId.ToString()), new Claim(ClaimTypes.Role, owner.Role)
    ], "push-subscription"));
}
