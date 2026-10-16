using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Facebook.Services;
using Shared.Security;
using StackExchange.Redis;
using System.Text.Json;

namespace Modules.Facebook.API;

[ApiController]
[Route("api/facebook/oauth")]
public sealed class FacebookOAuthController : ControllerBase
{
    private readonly IFacebookOAuthService _oauthService;
    private readonly IFacebookGraphService _graphService;
    private readonly IDatabase _redis;
    private readonly string _frontendUrl;

    public FacebookOAuthController(IFacebookOAuthService oauthService, IFacebookGraphService graphService,
        IConnectionMultiplexer redis, IConfiguration configuration)
    {
        _oauthService = oauthService;
        _graphService = graphService;
        _redis = redis.GetDatabase();
        _frontendUrl = configuration["FRONTEND_URL"] ?? "http://localhost:3000";
    }

    [HttpGet("login")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> Login([FromQuery] string projectId)
    {
        if (!Guid.TryParse(projectId, out var selectedProject)
            || !new ProjectAuthorizationService().CanManageProject(User, selectedProject)) return Forbid();
        var csrfToken = Guid.NewGuid().ToString("N");
        await _redis.StringSetAsync($"fb_oauth_csrf:{csrfToken}", projectId, TimeSpan.FromMinutes(10));
        return Ok(new { authorizationUrl = _oauthService.GetLoginUrl(projectId, csrfToken) });
    }

    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error = null, [FromQuery] string? error_description = null,
        [FromQuery] string? error_reason = null, [FromQuery] int? error_code = null)
    {
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            return ErrorPage(error_description ?? error ?? "Facebook authorization was not completed.");
        var parts = state.Split(':');
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out _)) return BadRequest("Invalid state format");
        var projectId = parts[0];
        var storedProjectId = await _redis.StringGetAsync($"fb_oauth_csrf:{parts[1]}");
        if (storedProjectId.IsNullOrEmpty || storedProjectId != projectId)
            return StatusCode(403, "Invalid or expired CSRF token");
        await _redis.KeyDeleteAsync($"fb_oauth_csrf:{parts[1]}");
        try
        {
            var shortLivedToken = await _oauthService.ExchangeCodeForTokenAsync(code);
            var token = await _oauthService.ExchangeForLongLivedTokenAsync(shortLivedToken);
            var pages = await _graphService.GetUserPagesAsync(token);
            return CallbackPage(new
            {
                type = "facebook-oauth-success", projectId, userAccessToken = token,
                pages = pages.ConvertAll(page => new { pageId = page.PageId, pageName = page.PageName, accessToken = page.AccessToken })
            }, "تم الربط بنجاح! يمكنك إغلاق هذه النافذة.");
        }
        catch (Exception)
        {
            return ErrorPage("تعذر إكمال ربط فيسبوك. حاول مرة أخرى.");
        }
    }

    private ContentResult ErrorPage(string message) =>
        CallbackPage(new { type = "facebook-oauth-error", error = message }, $"خطأ في الربط: {message}");

    private ContentResult CallbackPage(object payload, string message)
    {
        // Default JSON escaping prevents HTML parser termination inside the script element.
        var json = JsonSerializer.Serialize(payload);
        var origin = JsonSerializer.Serialize(_frontendUrl);
        var text = JsonSerializer.Serialize(message);
        return Content($$"""
            <!doctype html><html><head><meta charset="utf-8"><title>Facebook</title></head><body>
            <script>
            const payload = {{json}};
            if (window.opener) {
                window.opener.postMessage(payload, {{origin}});
                window.close();
            } else {
                document.body.textContent = {{text}};
            }
            </script></body></html>
            """, "text/html");
    }
}
