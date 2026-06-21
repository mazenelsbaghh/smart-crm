using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Modules.Facebook.Services
{
    public class FacebookGraphService : IFacebookGraphService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiVersion;
        private readonly ILogger<FacebookGraphService> _logger;

        public FacebookGraphService(IConfiguration configuration, ILogger<FacebookGraphService> logger)
        {
            _httpClient = new HttpClient();
            _apiVersion = configuration["FACEBOOK_GRAPH_API_VERSION"] ?? "v20.0";
            _logger = logger;
        }

        private string GraphUrl => $"https://graph.facebook.com/{_apiVersion}";

        public async Task SendMessageAsync(string pageId, string pageAccessToken, string recipientPSID, string message)
        {
            var url = $"{GraphUrl}/{pageId}/messages?access_token={pageAccessToken}";
            var payload = new
            {
                recipient = new { id = recipientPSID },
                message = new { text = message }
            };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[FacebookGraph] SendMessage failed: {StatusCode} {Body}", response.StatusCode, responseBody);
                throw new Exception($"Facebook SendMessage failed: {response.StatusCode} - {responseBody}");
            }

            _logger.LogInformation("[FacebookGraph] Message sent to PSID {PSID} via Page {PageId}", recipientPSID, pageId);
        }

        public async Task ReplyToCommentAsync(string pageAccessToken, string commentId, string message)
        {
            var url = $"{GraphUrl}/{commentId}/comments?access_token={pageAccessToken}";
            var payload = new { message };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[FacebookGraph] ReplyToComment failed: {StatusCode} {Body}", response.StatusCode, responseBody);
                throw new Exception($"Facebook ReplyToComment failed: {response.StatusCode} - {responseBody}");
            }

            _logger.LogInformation("[FacebookGraph] Replied to comment {CommentId}. Response: {Response}", commentId, responseBody);
        }

        public async Task ReactToCommentAsync(string pageAccessToken, string commentId, string reactionType = "LIKE")
        {
            var url = $"{GraphUrl}/{commentId}/reactions?reaction_type={reactionType.ToUpper()}&access_token={pageAccessToken}";
            var response = await _httpClient.PostAsync(url, null);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[FacebookGraph] ReactToComment failed (non-critical): {StatusCode} {Body}", response.StatusCode, responseBody);
                // Fallback to simple like
                var fallbackUrl = $"{GraphUrl}/{commentId}/likes?access_token={pageAccessToken}";
                await _httpClient.PostAsync(fallbackUrl, null);
                return;
            }

            _logger.LogInformation("[FacebookGraph] Reacted to comment {CommentId} with {Reaction}", commentId, reactionType);
        }

        public async Task SendPrivateReplyAsync(string pageId, string pageAccessToken, string commentId, string message)
        {
            var url = $"{GraphUrl}/{pageId}/messages?access_token={pageAccessToken}";
            var payload = new
            {
                recipient = new { comment_id = commentId },
                message = new { text = message }
            };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[FacebookGraph] SendPrivateReply failed: {StatusCode} {Body}", response.StatusCode, responseBody);
                // Private reply may fail if user hasn't opted in — don't crash
                return;
            }

            _logger.LogInformation("[FacebookGraph] Private reply sent for comment {CommentId}", commentId);
        }

        public async Task SubscribePageToAppAsync(string pageId, string pageAccessToken)
        {
            var url = $"{GraphUrl}/{pageId}/subscribed_apps?subscribed_fields=messages,feed&access_token={pageAccessToken}";
            var response = await _httpClient.PostAsync(url, null);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[FacebookGraph] SubscribePageToApp failed: {StatusCode} {Body}", response.StatusCode, responseBody);
                throw new Exception($"Facebook SubscribePageToApp failed: {response.StatusCode} - {responseBody}");
            }

            _logger.LogInformation("[FacebookGraph] Page {PageId} subscribed to app webhooks", pageId);
        }

        public async Task<List<FacebookPageInfo>> GetUserPagesAsync(string userAccessToken)
        {
            var url = $"{GraphUrl}/me/accounts?access_token={userAccessToken}&fields=id,name,access_token";
            var response = await _httpClient.GetAsync(url);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[FacebookGraph] GetUserPages failed: {StatusCode} {Body}", response.StatusCode, responseBody);
                throw new Exception($"Facebook GetUserPages failed: {response.StatusCode} - {responseBody}");
            }

            using var doc = JsonDocument.Parse(responseBody);
            var pages = new List<FacebookPageInfo>();

            if (doc.RootElement.TryGetProperty("data", out var dataArray))
            {
                foreach (var page in dataArray.EnumerateArray())
                {
                    pages.Add(new FacebookPageInfo
                    {
                        PageId = page.GetProperty("id").GetString() ?? "",
                        PageName = page.GetProperty("name").GetString() ?? "",
                        AccessToken = page.GetProperty("access_token").GetString() ?? ""
                    });
                }
            }

            _logger.LogInformation("[FacebookGraph] Found {Count} pages for user", pages.Count);
            return pages;
        }
    }
}
