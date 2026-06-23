using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Modules.Facebook.Services
{
    public class FacebookOAuthService : IFacebookOAuthService
    {
        private readonly string _appId;
        private readonly string _appSecret;
        private readonly string _redirectUri;
        private readonly string _apiVersion;
        private readonly HttpClient _httpClient;
        private readonly ILogger<FacebookOAuthService> _logger;

        private const string RequiredScopes = "pages_show_list,pages_read_engagement,pages_manage_engagement,pages_messaging,pages_manage_metadata";

        public FacebookOAuthService(IConfiguration configuration, ILogger<FacebookOAuthService> logger)
        {
            _appId = configuration["FACEBOOK_APP_ID"] ?? throw new ArgumentException("FACEBOOK_APP_ID is required");
            _appSecret = configuration["FACEBOOK_APP_SECRET"] ?? throw new ArgumentException("FACEBOOK_APP_SECRET is required");
            _redirectUri = configuration["FACEBOOK_OAUTH_REDIRECT_URI"] ?? throw new ArgumentException("FACEBOOK_OAUTH_REDIRECT_URI is required");
            _apiVersion = configuration["FACEBOOK_GRAPH_API_VERSION"] ?? "v20.0";
            _httpClient = new HttpClient();
            _logger = logger;
        }

        public string GetLoginUrl(string projectId, string csrfToken)
        {
            var state = $"{projectId}:{csrfToken}";
            var encodedRedirectUri = HttpUtility.UrlEncode(_redirectUri);
            var encodedState = HttpUtility.UrlEncode(state);

            return $"https://www.facebook.com/{_apiVersion}/dialog/oauth?" +
                   $"client_id={_appId}" +
                   $"&redirect_uri={encodedRedirectUri}" +
                   $"&scope={RequiredScopes}" +
                   $"&response_type=code" +
                   $"&state={encodedState}" +
                   $"&auth_type=rerequest";
        }

        public async Task<string> ExchangeCodeForTokenAsync(string code)
        {
            var url = $"https://graph.facebook.com/{_apiVersion}/oauth/access_token?" +
                      $"client_id={_appId}" +
                      $"&redirect_uri={HttpUtility.UrlEncode(_redirectUri)}" +
                      $"&client_secret={_appSecret}" +
                      $"&code={HttpUtility.UrlEncode(code)}";

            var response = await _httpClient.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[FacebookOAuth] Code exchange failed: {Body}", body);
                throw new Exception($"Facebook OAuth code exchange failed: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var accessToken = doc.RootElement.GetProperty("access_token").GetString();

            _logger.LogInformation("[FacebookOAuth] Code exchanged for short-lived token");
            return accessToken ?? throw new Exception("No access_token in Facebook response");
        }

        public async Task<string> ExchangeForLongLivedTokenAsync(string shortLivedToken)
        {
            var url = $"https://graph.facebook.com/{_apiVersion}/oauth/access_token?" +
                      $"grant_type=fb_exchange_token" +
                      $"&client_id={_appId}" +
                      $"&client_secret={_appSecret}" +
                      $"&fb_exchange_token={HttpUtility.UrlEncode(shortLivedToken)}";

            var response = await _httpClient.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("[FacebookOAuth] Long-lived token exchange failed: {Body}", body);
                throw new Exception($"Facebook long-lived token exchange failed: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            var longLivedToken = doc.RootElement.GetProperty("access_token").GetString();

            _logger.LogInformation("[FacebookOAuth] Exchanged for long-lived token (~60 days)");
            return longLivedToken ?? throw new Exception("No access_token in long-lived token response");
        }
    }
}
