using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using FirebaseAdmin.Messaging;

namespace Modules.Projects.API
{
    [ApiController]
    [Route("api/projects/{projectId}/fcm-tokens")]
    public class FcmController : ControllerBase
    {
        private readonly IDatabase _redis;

        public FcmController(IConnectionMultiplexer redis)
        {
            _redis = redis.GetDatabase();
        }

        [HttpPost]
        public async Task<IActionResult> RegisterToken(Guid projectId, [FromBody] FcmTokenRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Token))
            {
                return BadRequest(new { error = "Token is required." });
            }

            var redisKey = $"fcm_tokens:{projectId}";
            
            // Add token to the project's Redis Set
            await _redis.SetAddAsync(redisKey, request.Token);
            
            // Set token expiration of 30 days (so stale/inactive tokens eventually expire)
            await _redis.KeyExpireAsync(redisKey, TimeSpan.FromDays(30));

            Console.WriteLine($"[FcmController] Registered FCM token for project: {projectId}");

            return Ok(new { message = "FCM token registered successfully." });
        }

        [HttpPost("test")]
        public async Task<IActionResult> SendTestNotification(Guid projectId)
        {
            var redisKey = $"fcm_tokens:{projectId}";
            var tokens = await _redis.SetMembersAsync(redisKey);
            if (tokens.Length == 0)
            {
                return BadRequest(new { error = "No registered devices found for this project. Please open the app first to register your device." });
            }

            var tokenList = tokens.Select(t => t.ToString()).ToList();
            var fcmMessage = new MulticastMessage
            {
                Tokens = tokenList,
                Notification = new Notification
                {
                    Title = "تنبيه تجريبي 🧪",
                    Body = "هذا إشعار تجريبي من نظام الإشعارات للتأكد من عمل الميزة بنجاح!"
                },
                Data = new Dictionary<string, string>
                {
                    { "type", "Test" },
                    { "projectId", projectId.ToString() }
                }
            };

            try
            {
                var response = await FirebaseMessaging.DefaultInstance.SendMulticastAsync(fcmMessage);
                return Ok(new
                {
                    message = $"Sent test notification to {tokenList.Count} devices.",
                    successCount = response.SuccessCount,
                    failureCount = response.FailureCount
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"FCM Send failed: {ex.Message}" });
            }
        }
    }

    public class FcmTokenRequest
    {
        public string Token { get; set; } = string.Empty;
    }
}
