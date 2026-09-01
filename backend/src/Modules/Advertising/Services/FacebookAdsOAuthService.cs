using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Shared.Infrastructure;
using StackExchange.Redis;

namespace Modules.Advertising.Services;

public sealed record OAuthStartResult(string AuthorizationUrl);
internal sealed record OAuthState(Guid ProjectId, Guid UserId, DateTime CreatedAtUtc);

public sealed class FacebookAdsOAuthService(IConnectionMultiplexer redis, IOptions<AdvertisingOptions> options, MetaAdsClient meta,
    AdvertisingSecretVault vault, AppDbContext db, AdvertisingAuditService audit)
{
    private readonly MetaOptions _options = options.Value.Meta;

    public async Task<OAuthStartResult> StartAsync(Guid projectId, Guid userId)
    {
        if (_options.UseMock)
        {
            var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId);
            if (connection is null)
            {
                connection = new AdvertisingConnection { ProjectId = projectId, CreatedByUserId = userId, State = AdvertisingConnectionState.PendingSelection, ProtectedAccessToken = vault.Protect($"mock:{projectId:N}") };
                db.AdvertisingConnections.Add(connection);
                audit.Append(new(projectId, "Credential", "MockCredentialCreated", nameof(AdvertisingConnection), connection.Id.ToString(),
                    "User", userId, "{}", connection.Id));
                await db.SaveChangesAsync();
            }
            return new("mock://facebook-ads/authorized");
        }
        if (string.IsNullOrWhiteSpace(_options.AppId) || string.IsNullOrWhiteSpace(_options.OAuthRedirectUri)) throw new InvalidOperationException("Meta OAuth is not configured.");
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var stored = await redis.GetDatabase().StringSetAsync($"ads:oauth:{state}", JsonSerializer.Serialize(new OAuthState(projectId, userId, DateTime.UtcNow)), TimeSpan.FromMinutes(10), When.NotExists);
        if (!stored) throw new InvalidOperationException("Could not reserve a one-time Meta OAuth state.");
        // The WhatsApp channel is owned by the already-connected Baileys gateway. Requesting
        // WhatsApp Business Platform scopes here makes standard Facebook Login reject the
        // entire dialog unless a separate Meta Business Login configuration exists.
        var scopes = "ads_read,ads_management,business_management,pages_show_list,pages_read_engagement";
        var url = $"https://www.facebook.com/{_options.GraphApiVersion}/dialog/oauth?client_id={Uri.EscapeDataString(_options.AppId)}&redirect_uri={Uri.EscapeDataString(_options.OAuthRedirectUri)}&state={state}&scope={Uri.EscapeDataString(scopes)}&response_type=code";
        return new(url);
    }

    public async Task<(Guid ProjectId, Guid ConnectionId)> CompleteAsync(string state, string code, CancellationToken cancellationToken)
    {
        var cache = redis.GetDatabase();
        var key = $"ads:oauth:{state}";
        var raw = await cache.StringGetDeleteAsync(key);
        if (raw.IsNullOrEmpty) throw new UnauthorizedAccessException("OAuth state is invalid or expired.");
        var oauth = JsonSerializer.Deserialize<OAuthState>(raw!) ?? throw new UnauthorizedAccessException("OAuth state is invalid.");
        var token = await meta.ExchangeCodeAsync(code, cancellationToken);
        var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == oauth.ProjectId, cancellationToken);
        if (connection is null)
        {
            connection = new AdvertisingConnection { ProjectId = oauth.ProjectId, CreatedByUserId = oauth.UserId };
            db.AdvertisingConnections.Add(connection);
        }
        connection.ProtectedAccessToken = vault.Protect(token);
        connection.State = AdvertisingConnectionState.PendingSelection;
        connection.LastValidatedAtUtc = DateTime.UtcNow;
        audit.Append(new(oauth.ProjectId, "Credential", "MetaCredentialStored", nameof(AdvertisingConnection), connection.Id.ToString(),
            "User", oauth.UserId, "{}", connection.Id));
        await db.SaveChangesAsync(cancellationToken);
        return (oauth.ProjectId, connection.Id);
    }
}
