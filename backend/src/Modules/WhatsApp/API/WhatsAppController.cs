using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.WhatsApp.API
{
    [ApiController]
    [Route("api/whatsapp")]
    public class WhatsAppController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _gatewayUrl;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public WhatsAppController(IConfiguration configuration)
        {
            _httpClient = new HttpClient();
            _gatewayUrl = configuration["WhatsAppGateway:Url"] ?? "http://whatsapp-gateway:3000";
        }

        [HttpPost("session/start")]
        public async Task<IActionResult> StartSession([FromBody] StartSessionRequest request)
        {
            var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/session/start", content);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpGet("session/qr")]
        public async Task<IActionResult> GetQR([FromQuery] Guid projectId)
        {
            var response = await _httpClient.GetAsync($"{_gatewayUrl}/api/whatsapp/session/qr?projectId={projectId}");
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpGet("session/status")]
        public async Task<IActionResult> GetStatus([FromQuery] Guid projectId)
        {
            var response = await _httpClient.GetAsync($"{_gatewayUrl}/api/whatsapp/session/status?projectId={projectId}");
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest request)
        {
            var payload = JsonSerializer.Serialize(request, _jsonOptions);
            var response = await Shared.Infrastructure.GatewayRetryHelper.PostWithRetryAsync(_httpClient, $"{_gatewayUrl}/api/whatsapp/send", payload);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpPost("session/mock")]
        public async Task<IActionResult> MockSession([FromBody] MockSessionRequest request)
        {
            var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/session/mock", content);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpGet("mock/sent")]
        public async Task<IActionResult> GetMockSentMessages()
        {
            var response = await _httpClient.GetAsync($"{_gatewayUrl}/api/whatsapp/mock/sent");
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpPost("mock/clear")]
        public async Task<IActionResult> ClearMockSentMessages()
        {
            var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/mock/clear", null);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }

        [HttpPost("session/disconnect")]
        public async Task<IActionResult> DisconnectSession([FromBody] DisconnectSessionRequest request)
        {
            var content = new StringContent(JsonSerializer.Serialize(request, _jsonOptions), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_gatewayUrl}/api/whatsapp/session/disconnect", content);
            var result = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, JsonDocument.Parse(result));
        }
    }

    public class StartSessionRequest
    {
        public Guid ProjectId { get; set; }
    }

    public class SendMessageRequest
    {
        public Guid ProjectId { get; set; }
        public string To { get; set; } = default!;
        public string Message { get; set; } = default!;
        public string[]? Buttons { get; set; }
    }

    public class MockSessionRequest
    {
        public Guid ProjectId { get; set; }
        public string Status { get; set; } = default!;
        public string? PhoneNumber { get; set; }
    }

    public class DisconnectSessionRequest
    {
        public Guid ProjectId { get; set; }
    }
}
