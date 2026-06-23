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

        public static string MapToFacebookReaction(string? input)
        {
            if (string.IsNullOrEmpty(input)) return "LOVE";
            
            var normalized = input.Trim().ToUpperInvariant();
            if (normalized == "LIKE" || normalized == "LOVE" || normalized == "CARE" || 
                normalized == "HAHA" || normalized == "WOW" || normalized == "SAD" || normalized == "ANGRY")
            {
                return normalized;
            }

            if (input.Contains("❤️") || input.Contains("💖") || input.Contains("💝") || input.Contains("💕") || input.Contains("😍"))
                return "LOVE";
            if (input.Contains("👍") || input.Contains("👌") || input.Contains("👏") || input.Contains("✔️"))
                return "LIKE";
            if (input.Contains("😮") || input.Contains("😲") || input.Contains("😂") || input.Contains("😆") || input.Contains("🤣"))
                return "WOW";
            if (input.Contains("😢") || input.Contains("😭") || input.Contains("😞"))
                return "SAD";
            if (input.Contains("😡") || input.Contains("😠"))
                return "ANGRY";

            return "LOVE"; // Default to LOVE as per user request
        }

        public async Task ReactToCommentAsync(string pageAccessToken, string commentId, string reactionType = "LOVE")
        {
            var mappedReaction = MapToFacebookReaction(reactionType);
            var url = $"{GraphUrl}/{commentId}/reactions?reaction_type={mappedReaction}&access_token={pageAccessToken}";
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

            _logger.LogInformation("[FacebookGraph] Reacted to comment {CommentId} with {Reaction}", commentId, mappedReaction);
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
            var url = $"{GraphUrl}/me/accounts?access_token={userAccessToken}&fields=id,name,access_token&limit=100";
            var pages = new List<FacebookPageInfo>();
            string? nextUrl = url;

            while (!string.IsNullOrEmpty(nextUrl))
            {
                var response = await _httpClient.GetAsync(nextUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("[FacebookGraph] GetUserPages failed: {StatusCode} {Body}", response.StatusCode, responseBody);
                    throw new Exception($"Facebook GetUserPages failed: {response.StatusCode} - {responseBody}");
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.TryGetProperty("data", out var dataArray))
                {
                    foreach (var page in dataArray.EnumerateArray())
                    {
                        var pageId = page.GetProperty("id").GetString() ?? "";
                        var pageName = page.GetProperty("name").GetString() ?? "";
                        var hasToken = page.TryGetProperty("access_token", out var tokenProp);
                        var accessTokenVal = hasToken ? (tokenProp.GetString() ?? "") : "";

                        _logger.LogInformation("[FacebookGraph] Page Found - Name: {Name}, ID: {Id}, HasToken: {HasToken}", pageName, pageId, !string.IsNullOrEmpty(accessTokenVal));

                        pages.Add(new FacebookPageInfo
                        {
                            PageId = pageId,
                            PageName = pageName,
                            AccessToken = accessTokenVal
                        });
                    }
                }

                nextUrl = null;
                if (doc.RootElement.TryGetProperty("paging", out var pagingElement) &&
                    pagingElement.TryGetProperty("next", out var nextElement))
                {
                    nextUrl = nextElement.GetString();
                }
            }

            _logger.LogInformation("[FacebookGraph] Found {Count} pages for user", pages.Count);
            return pages;
        }
    }
}
