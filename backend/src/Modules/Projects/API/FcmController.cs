using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FirebaseAdmin.Messaging;
using Modules.Projects.Services;
using Shared.Security;

namespace Modules.Projects.API;

[ApiController]
[Authorize]
[ProjectPermission("read")]
[Route("api/projects/{projectId}/fcm-tokens")]
public sealed class FcmController(ProjectPushSubscriptions subscriptions) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> RegisterToken(Guid projectId, FcmTokenRequest request)
    {
        if (!new ProjectAuthorizationService().CanRead(User, projectId)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Token) || request.Token.Length > 4096) return BadRequest("Invalid device token.");
        await subscriptions.RegisterAsync(User, request.Token);
        return Ok(new { message = "FCM token registered successfully." });
    }

    [HttpDelete]
    public async Task<IActionResult> RemoveToken(Guid projectId, FcmTokenRequest request)
    {
        if (!new ProjectAuthorizationService().CanRead(User, projectId)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Token)) return BadRequest("Device token is required.");
        return await subscriptions.RemoveAsync(User, request.Token) ? NoContent() : Forbid();
    }

    [HttpPost("test")]
    [Authorize(Roles = "Owner,Admin")]
    public async Task<IActionResult> SendTestNotification(Guid projectId)
    {
        if (!new ProjectAuthorizationService().CanManageProject(User, projectId)) return Forbid();
        var tokens = await subscriptions.ActiveTokensAsync(projectId);
        if (tokens.Count == 0) return BadRequest(new { error = "No registered devices found for this project." });
        var response = await FirebaseMessaging.DefaultInstance.SendEachForMulticastAsync(new MulticastMessage
        {
            Tokens = tokens,
            Notification = new Notification { Title = "تنبيه تجريبي", Body = "تم تفعيل الإشعارات." },
            Data = new Dictionary<string, string> { ["type"] = "Test", ["projectId"] = projectId.ToString() }
        });
        return Ok(new { successCount = response.SuccessCount, failureCount = response.FailureCount });
    }
}

public sealed class FcmTokenRequest
{
    public string Token { get; set; } = string.Empty;
}
