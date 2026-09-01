using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Modules.Advertising.Services;

namespace Modules.Advertising.API;

[ApiController]
[AllowAnonymous]
[Route("api/ad-manager/meta/oauth/callback")]
public sealed class FacebookAdsOAuthCallbackController(FacebookAdsOAuthService oauth, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Callback([FromQuery] string state, [FromQuery] string code, CancellationToken cancellationToken)
    {
        try
        {
            var result = await oauth.CompleteAsync(state, code, cancellationToken);
            var frontend = (configuration["FRONTEND_URL"] ?? "http://localhost:3000").TrimEnd('/');
            return Redirect($"{frontend}/management/ad-manager?meta=connected&result={result.ConnectionId}");
        }
        catch (UnauthorizedAccessException)
        {
            var frontend = (configuration["FRONTEND_URL"] ?? "http://localhost:3000").TrimEnd('/');
            return Redirect($"{frontend}/management/ad-manager?meta=failed");
        }
        catch (HttpRequestException)
        {
            var frontend = (configuration["FRONTEND_URL"] ?? "http://localhost:3000").TrimEnd('/');
            return Redirect($"{frontend}/management/ad-manager?meta=failed");
        }
        catch (InvalidOperationException)
        {
            var frontend = (configuration["FRONTEND_URL"] ?? "http://localhost:3000").TrimEnd('/');
            return Redirect($"{frontend}/management/ad-manager?meta=failed");
        }
    }
}
