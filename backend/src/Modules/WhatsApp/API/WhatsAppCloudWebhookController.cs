using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;

namespace Modules.WhatsApp.API;

[ApiController]
[AllowAnonymous]
[Route("api/integrations/whatsapp/cloud")]
public sealed class WhatsAppCloudWebhookController(AppDbContext db, IOptions<AdvertisingOptions> options,
    IAdvertisingReferralProtector protector) : ControllerBase
{
    [HttpGet]
    public IActionResult Verify([FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode != "subscribe" || string.IsNullOrWhiteSpace(challenge) ||
            string.IsNullOrWhiteSpace(options.Value.WhatsAppCloud.VerifyToken) ||
            !FixedEquals(token ?? string.Empty, options.Value.WhatsAppCloud.VerifyToken)) return Forbid();
        return Content(challenge, "text/plain", Encoding.UTF8);
    }

    [HttpPost]
    [RequestSizeLimit(1_048_576)]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var settings = options.Value.WhatsAppCloud;
        if (string.IsNullOrWhiteSpace(settings.AppSecret)) return StatusCode(503, new { code = "ADS_WHATSAPP_WEBHOOK_NOT_CONFIGURED" });
        await using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > settings.MaximumWebhookBodyBytes) return StatusCode(413);
        var raw = buffer.ToArray();
        if (!VerifySignature(raw, Request.Headers["X-Hub-Signature-256"].FirstOrDefault(), settings.AppSecret))
            return Unauthorized(new { code = "ADS_WHATSAPP_SIGNATURE_INVALID" });

        using var document = JsonDocument.Parse(raw);
        var accepted = 0;
        foreach (var (wabaId, change) in Changes(document.RootElement))
        {
            var phoneId = Text(change, "metadata", "phone_number_id");
            if (string.IsNullOrWhiteSpace(wabaId) || string.IsNullOrWhiteSpace(phoneId)) continue;
            var routes = await db.WhatsAppInboundRouteProjections.IgnoreQueryFilters()
                .Where(route => route.WabaExternalId == wabaId && route.PhoneNumberExternalId == phoneId
                    && route.State == "Active").ToListAsync(cancellationToken);
            if (routes.Count != 1) return Conflict(new { code = routes.Count == 0 ? "ADS_WHATSAPP_ROUTE_NOT_FOUND" : "ADS_WHATSAPP_ROUTE_AMBIGUOUS" });
            var route = routes[0];
            foreach (var message in Array(change, "messages"))
            {
                var messageId = Text(message, "id");
                if (string.IsNullOrWhiteSpace(messageId)) continue;
                var eventId = DeterministicGuid($"meta-cloud:{messageId}");
                if (await db.IntegrationOutboxMessages.AnyAsync(item => item.EventId == eventId, cancellationToken)) continue;
                var sender = Text(message, "from") ?? string.Empty;
                var occurred = long.TryParse(Text(message, "timestamp"), out var unix)
                    ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime : DateTime.UtcNow;
                var referral = message.TryGetProperty("referral", out var referralJson) ? referralJson : default;
                var ctwaClid = Text(referral, "ctwa_clid");
                var providerAdId = Text(referral, "source_id");
                var referralState = !string.IsNullOrWhiteSpace(ctwaClid) ? "CtwaClid" : referral.ValueKind == JsonValueKind.Object ? "OpaquePayloadOnly" : "Missing";
                var protectedReferral = protector.ProtectInboundJson(JsonSerializer.Serialize(new
                {
                    identifierState = referralState, ctwaClid, providerAdId,
                    opaquePayloadHash = referralState == "OpaquePayloadOnly" ? protector.Hash(referral.GetRawText()) : null,
                    gatewayType = "CloudApi"
                }));
                IntegrationOutbox.Enqueue(db, new WhatsAppInboundMessageReceived
                {
                    Id = eventId, ProjectId = route.ProjectId, SourceAggregateType = "WhatsAppProviderMessage",
                    SourceAggregateId = eventId, SourceVersion = 1, CorrelationId = eventId,
                    DestinationId = route.DestinationId, DestinationVersion = route.DestinationVersion,
                    ProviderMessageId = messageId, MessageOccurredAtUtc = occurred,
                    ProtectedSenderReference = protector.ProtectInboundJson(sender),
                    NormalizedContentJson = JsonSerializer.Serialize(new { name = string.Empty,
                        content = MessageContent(message), messageType = MessageType(message) }),
                    ProtectedReferralJson = protectedReferral
                });
                accepted++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { accepted });
    }

    private static IEnumerable<(string WabaId, JsonElement Value)> Changes(JsonElement root) => Array(root, "entry")
        .SelectMany(entry => Array(entry, "changes")
            .Where(change => change.TryGetProperty("value", out _))
            .Select(change => (Text(entry, "id") ?? string.Empty, change.GetProperty("value"))));
    private static IEnumerable<JsonElement> Array(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];
    private static string? Text(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) ? value.ToString() : null;
    private static string? Text(JsonElement root, string parent, string name) => root.ValueKind == JsonValueKind.Object && root.TryGetProperty(parent, out var value) ? Text(value, name) : null;
    private static string MessageType(JsonElement message) => Text(message, "type") ?? "text";
    private static string MessageContent(JsonElement message) => MessageType(message) switch
    {
        "text" => Text(message, "text", "body") ?? string.Empty,
        "image" => "[Image]", "audio" => "[Voice Note]", "video" => "[Video]", "document" => "[Document]", _ => "[WhatsApp Message]"
    };
    private static bool VerifySignature(byte[] body, string? provided, string secret)
    {
        if (string.IsNullOrWhiteSpace(provided) || !provided.StartsWith("sha256=", StringComparison.Ordinal)) return false;
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body);
        try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(provided[7..])); }
        catch (FormatException) { return false; }
    }
    private static bool FixedEquals(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value)).AsSpan(0, 16));
}
