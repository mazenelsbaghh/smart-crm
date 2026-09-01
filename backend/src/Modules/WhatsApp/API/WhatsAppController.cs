using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Services;
using Shared.Security;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Modules.WhatsApp.API
{
    [ApiController]
    [Authorize]
    [Route("api/whatsapp")]
    public class WhatsAppController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _gatewayUrl;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly IProjectAuthorizationService _authorization;
        private readonly WhatsAppAccountService _accountService;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public WhatsAppController(
            IConfiguration configuration,
            IHostEnvironment hostEnvironment,
            IProjectAuthorizationService authorization,
            IHttpClientFactory httpClientFactory,
            WhatsAppAccountService accountService)
        {
            _httpClient = httpClientFactory.CreateClient();
            _gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
            _hostEnvironment = hostEnvironment;
            _authorization = authorization;
            _accountService = accountService;
        }

        [HttpGet("accounts")]
        public async Task<IActionResult> GetAccounts(
            [FromQuery] Guid projectId,
            CancellationToken cancellationToken = default)
        {
            var accessFailure = ReadAccessFailure(projectId);
            if (accessFailure is not null) return accessFailure;

            var accounts = await _accountService.ListAsync(projectId, cancellationToken);
            return Ok(accounts.Select(AccountResponse).ToArray());
        }

        [HttpPost("accounts")]
        public async Task<IActionResult> CreateAccount(
            [FromBody] CreateWhatsAppAccountRequest request,
            CancellationToken cancellationToken = default)
        {
            var accessFailure = ManagementAccessFailure(request.ProjectId);
            if (accessFailure is not null) return accessFailure;

            var account = await _accountService.CreateAsync(
                request.ProjectId,
                request.Name,
                cancellationToken);
            return Created(
                $"/api/whatsapp/accounts?projectId={Uri.EscapeDataString(account.ProjectId.ToString())}",
                AccountResponse(account));
        }

        [HttpPut("accounts/{accountId:guid}")]
        public async Task<IActionResult> UpdateAccount(
            Guid accountId,
            [FromBody] UpdateWhatsAppAccountRequest request,
            CancellationToken cancellationToken = default)
        {
            var accessFailure = ManagementAccessFailure(request.ProjectId);
            if (accessFailure is not null) return accessFailure;

            var account = await _accountService.UpdateAsync(
                request.ProjectId,
                accountId,
                request.Name,
                request.IsDefault,
                cancellationToken);
            return account is null ? AccountNotFound(accountId) : Ok(AccountResponse(account));
        }

        [HttpPost("session/start")]
        public async Task<IActionResult> StartSession([FromBody] StartSessionRequest request)
        {
            var accessFailure = ManagementAccessFailure(request.ProjectId);
            if (accessFailure is not null) return accessFailure;

            var account = await ResolveAccountAsync(request.ProjectId, request.WhatsAppAccountId);
            if (account is null) return AccountNotFound(request.WhatsAppAccountId);
            request.WhatsAppAccountId = account.GatewayAccountId;

            using var content = JsonContent(request);
            using var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/session/start", content);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpGet("session/qr")]
        public async Task<IActionResult> GetQR(
            [FromQuery] Guid projectId,
            [FromQuery] Guid? whatsappAccountId = null)
        {
            var accessFailure = ManagementAccessFailure(projectId);
            if (accessFailure is not null) return accessFailure;

            var account = await ResolveAccountAsync(projectId, whatsappAccountId);
            if (account is null) return AccountNotFound(whatsappAccountId);

            using var response = await _httpClient.GetAsync(
                SessionEndpoint("qr", projectId, account.GatewayAccountId));
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpGet("session/status")]
        public async Task<IActionResult> GetStatus(
            [FromQuery] Guid projectId,
            [FromQuery] Guid? whatsappAccountId = null,
            CancellationToken cancellationToken = default)
        {
            var accessFailure = ReadAccessFailure(projectId);
            if (accessFailure is not null) return accessFailure;

            var account = await ResolveAccountAsync(projectId, whatsappAccountId, cancellationToken);
            if (account is null) return AccountNotFound(whatsappAccountId);

            using var gatewayTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            gatewayTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                using var response = await _httpClient.GetAsync(
                    SessionEndpoint("status", projectId, account.GatewayAccountId),
                    gatewayTimeout.Token);
                var result = await response.Content.ReadAsStringAsync(gatewayTimeout.Token);
                return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return StatusCode(504, new { code = "WHATSAPP_GATEWAY_TIMEOUT" });
            }
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            var accessFailure = ReadAccessFailure(request.ProjectId);
            if (accessFailure is not null) return accessFailure;

            var account = await ResolveAccountAsync(request.ProjectId, request.WhatsAppAccountId);
            if (account is null) return AccountNotFound(request.WhatsAppAccountId);
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                return BadRequest(new { code = "IDEMPOTENCY_KEY_REQUIRED" });
            if (!request.ExpectedConnectedAt.HasValue)
                return BadRequest(new { code = "EXPECTED_CONNECTED_AT_REQUIRED" });
            request.WhatsAppAccountId = account.GatewayAccountId;

            var payload = JsonSerializer.Serialize(request, _jsonOptions);
            using var response = await Shared.Infrastructure.GatewayRetryHelper.PostOnceAsync(
                _httpClient,
                $"{_gatewayUrl}/api/whatsapp/send",
                payload);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpPost("session/mock")]
        public async Task<IActionResult> MockSession([FromBody] MockSessionRequest request)
        {
            var accessFailure = MockAccessFailure(request.ProjectId);
            if (accessFailure is not null) return accessFailure;

            var account = await ResolveAccountAsync(request.ProjectId, request.WhatsAppAccountId);
            if (account is null) return AccountNotFound(request.WhatsAppAccountId);
            request.WhatsAppAccountId = account.GatewayAccountId;

            using var content = JsonContent(request);
            using var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/session/mock", content);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpGet("mock/sent")]
        public async Task<IActionResult> GetMockSentMessages([FromQuery] Guid? whatsappAccountId = null)
        {
            var projectId = _authorization.GetProjectId(User) ?? Guid.Empty;
            var accessFailure = MockAccessFailure(projectId);
            if (accessFailure is not null) return accessFailure;
            var account = await ResolveAccountAsync(projectId, whatsappAccountId);
            if (account is null) return AccountNotFound(whatsappAccountId);
            var endpoint = $"{_gatewayUrl}/api/whatsapp/mock/sent?projectId={Uri.EscapeDataString(projectId.ToString())}";
            if (account.GatewayAccountId.HasValue)
                endpoint += $"&whatsappAccountId={Uri.EscapeDataString(account.GatewayAccountId.Value.ToString())}";
            using var response = await _httpClient.GetAsync(endpoint);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpPost("mock/clear")]
        public async Task<IActionResult> ClearMockSentMessages([FromQuery] Guid? whatsappAccountId = null)
        {
            var projectId = _authorization.GetProjectId(User) ?? Guid.Empty;
            var accessFailure = MockAccessFailure(projectId);
            if (accessFailure is not null) return accessFailure;
            var account = await ResolveAccountAsync(projectId, whatsappAccountId);
            if (account is null) return AccountNotFound(whatsappAccountId);
            using var content = JsonContent(new
            {
                projectId,
                whatsappAccountId = account.GatewayAccountId
            });
            using var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/mock/clear", content);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpPost("session/disconnect")]
        public async Task<IActionResult> DisconnectSession([FromBody] DisconnectSessionRequest request)
        {
            var accessFailure = ManagementAccessFailure(request.ProjectId);
            if (accessFailure is not null) return accessFailure;

            var account = await ResolveAccountAsync(request.ProjectId, request.WhatsAppAccountId);
            if (account is null) return AccountNotFound(request.WhatsAppAccountId);
            request.WhatsAppAccountId = account.GatewayAccountId;

            using var content = JsonContent(request);
            using var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/session/disconnect", content);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        private async Task<ResolvedWhatsAppAccount?> ResolveAccountAsync(
            Guid projectId,
            Guid? accountId,
            CancellationToken cancellationToken = default)
        {
            var account = await _accountService.ResolveAsync(projectId, accountId, cancellationToken);
            return account is null
                ? null
                : new(WhatsAppAccountService.GatewayAccountId(projectId, account.Id));
        }

        private string SessionEndpoint(string operation, Guid projectId, Guid? gatewayAccountId)
        {
            var endpoint = $"{_gatewayUrl}/api/whatsapp/session/{operation}?projectId={Uri.EscapeDataString(projectId.ToString())}";
            return gatewayAccountId.HasValue
                ? $"{endpoint}&whatsappAccountId={Uri.EscapeDataString(gatewayAccountId.Value.ToString())}"
                : endpoint;
        }

        private static StringContent JsonContent<T>(T request) => new(
            JsonSerializer.Serialize(request, _jsonOptions),
            Encoding.UTF8,
            "application/json");

        private static object AccountResponse(WhatsAppAccount account) => new
        {
            account.Id,
            account.ProjectId,
            account.Name,
            account.IsDefault
        };

        private static NotFoundObjectResult AccountNotFound(Guid? accountId) => new(new
        {
            code = "WHATSAPP_ACCOUNT_NOT_FOUND",
            whatsappAccountId = accountId
        });

        private IActionResult? MockAccessFailure(Guid projectId)
        {
            if (!_hostEnvironment.IsDevelopment()) return NotFound();
            return ManagementAccessFailure(projectId);
        }

        private IActionResult? ReadAccessFailure(Guid projectId) =>
            _authorization.CanRead(User, projectId) ? null : Forbid();

        private IActionResult? ManagementAccessFailure(Guid projectId) =>
            _authorization.CanManageProject(User, projectId) ? null : Forbid();

        private sealed record ResolvedWhatsAppAccount(Guid? GatewayAccountId);
    }

    public sealed class CreateWhatsAppAccountRequest
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class UpdateWhatsAppAccountRequest
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
    }

    public class StartSessionRequest
    {
        public Guid ProjectId { get; set; }

        [JsonPropertyName("whatsappAccountId")]
        public Guid? WhatsAppAccountId { get; set; }
    }

    public class SendMessageRequest
    {
        public Guid ProjectId { get; set; }
        public string To { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string[]? Buttons { get; set; }
        public string? IdempotencyKey { get; set; }
        public DateTimeOffset? ExpectedConnectedAt { get; set; }

        [JsonPropertyName("whatsappAccountId")]
        public Guid? WhatsAppAccountId { get; set; }
    }

    public class MockSessionRequest
    {
        public Guid ProjectId { get; set; }
        public string Status { get; set; } = default!;
        public string? PhoneNumber { get; set; }

        [JsonPropertyName("whatsappAccountId")]
        public Guid? WhatsAppAccountId { get; set; }
    }

    public class DisconnectSessionRequest
    {
        public Guid ProjectId { get; set; }

        [JsonPropertyName("whatsappAccountId")]
        public Guid? WhatsAppAccountId { get; set; }
    }
}
