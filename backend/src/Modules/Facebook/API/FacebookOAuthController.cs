using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Modules.Facebook.Services;
using StackExchange.Redis;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Modules.Facebook.API
{
    [ApiController]
    [Route("api/facebook/oauth")]
    public class FacebookOAuthController : ControllerBase
    {
        private readonly IFacebookOAuthService _oauthService;
        private readonly IFacebookGraphService _graphService;
        private readonly IDatabase _redis;
        private readonly string _frontendUrl;

        public FacebookOAuthController(
            IFacebookOAuthService oauthService,
            IFacebookGraphService graphService,
            IConnectionMultiplexer redis,
            IConfiguration configuration)
        {
            _oauthService = oauthService;
            _graphService = graphService;
            _redis = redis.GetDatabase();
            _frontendUrl = configuration["FRONTEND_URL"] ?? "http://localhost:3000";
        }

        /// <summary>
        /// Initiates Facebook OAuth Login flow — redirects to Facebook Login Dialog
        /// </summary>
        [HttpGet("login")]
        public async Task<IActionResult> Login([FromQuery] string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
                return BadRequest(new { error = "projectId is required" });

            // Generate CSRF token and store in Redis with 10min TTL
            var csrfToken = Guid.NewGuid().ToString("N");
            await _redis.StringSetAsync($"fb_oauth_csrf:{csrfToken}", projectId, TimeSpan.FromMinutes(10));

            var loginUrl = _oauthService.GetLoginUrl(projectId, csrfToken);
            return Redirect(loginUrl);
        }

        /// <summary>
        /// Facebook redirects here after user grants permissions.
        /// Exchanges code for token and returns page list via postMessage to opener.
        /// </summary>
        [HttpGet("callback")]
        public async Task<IActionResult> Callback([FromQuery] string code, [FromQuery] string state)
        {
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                return BadRequest("Missing code or state parameter");

            // Parse state: "projectId:csrfToken"
            var parts = state.Split(':');
            if (parts.Length != 2)
                return BadRequest("Invalid state format");

            var projectId = parts[0];
            var csrfToken = parts[1];

            // Validate CSRF token
            var storedProjectId = await _redis.StringGetAsync($"fb_oauth_csrf:{csrfToken}");
            if (storedProjectId.IsNullOrEmpty || storedProjectId != projectId)
                return StatusCode(403, "Invalid or expired CSRF token");

            // Clean up CSRF token
            await _redis.KeyDeleteAsync($"fb_oauth_csrf:{csrfToken}");

            try
            {
                // Exchange code for short-lived token
                var shortLivedToken = await _oauthService.ExchangeCodeForTokenAsync(code);

                // Exchange for long-lived token (~60 days)
                var longLivedToken = await _oauthService.ExchangeForLongLivedTokenAsync(shortLivedToken);

                // Get user's pages
                var pages = await _graphService.GetUserPagesAsync(longLivedToken);

                // Return HTML that sends data to opener via postMessage then closes
                var pagesJson = JsonSerializer.Serialize(pages.ConvertAll(p => new
                {
                    pageId = p.PageId,
                    pageName = p.PageName,
                    accessToken = p.AccessToken
                }));

                var html = $@"
<!DOCTYPE html>
<html>
<head><title>Facebook Connected</title></head>
<body>
<script>
    if (window.opener) {{
        window.opener.postMessage({{
            type: 'facebook-oauth-success',
            projectId: '{projectId}',
            userAccessToken: '{longLivedToken}',
            pages: {pagesJson}
        }}, '{_frontendUrl}');
        window.close();
    }} else {{
        document.body.innerHTML = '<h2>تم الربط بنجاح! يمكنك إغلاق هذه النافذة.</h2>';
    }}
</script>
</body>
</html>";

                return Content(html, "text/html");
            }
            catch (Exception ex)
            {
                var errorHtml = $@"
<!DOCTYPE html>
<html>
<head><title>Error</title></head>
<body>
<script>
    if (window.opener) {{
        window.opener.postMessage({{
            type: 'facebook-oauth-error',
            error: '{ex.Message.Replace("'", "\\'")}'
        }}, '{_frontendUrl}');
        window.close();
    }} else {{
        document.body.innerHTML = '<h2>خطأ في الربط: {ex.Message}</h2>';
    }}
</script>
</body>
</html>";
                return Content(errorHtml, "text/html");
            }
        }
    }
}
