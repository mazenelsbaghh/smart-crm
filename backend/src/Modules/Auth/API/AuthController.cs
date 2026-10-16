using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Domain;
using Modules.Auth.Services;
using Shared.Infrastructure;
using Shared.Security;
using System;
using System.Threading.Tasks;

namespace Modules.Auth.API
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher _passwordHasher;
        private readonly IJwtService _jwtService;

        public AuthController(AppDbContext context, IPasswordHasher passwordHasher, IJwtService jwtService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtService = jwtService;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public IActionResult Register([FromBody] RegisterRequest _) =>
            StatusCode(StatusCodes.Status403Forbidden, new
            {
                code = "REGISTRATION_DISABLED",
                error = "Public registration is disabled. Contact an administrator for access."
            });

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Email == request.Email);
            if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
            {
                return Unauthorized(new { error = "Invalid email or password" });
            }

            var refreshToken = new RefreshToken
            {
                UserId = user.Id,
                ProjectId = user.ProjectId,
                Token = Guid.NewGuid().ToString(),
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _context.RefreshTokens.Add(refreshToken);
            refreshToken.SessionId = refreshToken.Id;
            await _context.SaveChangesAsync();
            var accessToken = _jwtService.GenerateToken(new(user.Id, user.Email, user.Role, user.ProjectId, refreshToken.Id));

            return Ok(new
            {
                accessToken,
                refreshToken = refreshToken.Token,
                expiresInSeconds = JwtSettings.LifetimeMinutes * 60,
                user = new
                {
                    user.Id,
                    user.Email,
                    user.Role
                }
            });
        }

        [HttpPost("refresh")]
        [AllowAnonymous]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            var token = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.Token == request.RefreshToken);
            if (token == null || !token.IsActive)
            {
                return Unauthorized(new { code = "REFRESH_TOKEN_INVALID", error = "Invalid or expired refresh token" });
            }

            var user = await _context.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == token.UserId);
            if (user == null)
            {
                return Unauthorized(new { code = "REFRESH_TOKEN_INVALID", error = "User not found" });
            }

            var projectId = token.ProjectId ?? user.ProjectId;
            var role = await ProjectAccess.RoleFor(_context, user, projectId);
            if (role is null)
                return Unauthorized(new { code = "REFRESH_TOKEN_INVALID", error = "Project access has been removed" });

            token.RevokedAt = DateTime.UtcNow;

            var newRefreshToken = new RefreshToken
            {
                UserId = user.Id,
                ProjectId = projectId,
                SessionId = token.SessionId ?? token.Id,
                Token = Guid.NewGuid().ToString(),
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };

            _context.RefreshTokens.Add(newRefreshToken);
            await _context.SaveChangesAsync();
            var newAccessToken = _jwtService.GenerateToken(new(user.Id, user.Email, role, projectId, newRefreshToken.SessionId!.Value));

            return Ok(new
            {
                accessToken = newAccessToken,
                refreshToken = newRefreshToken.Token,
                expiresInSeconds = JwtSettings.LifetimeMinutes * 60
            });
        }

        [HttpPost("logout")]
        [AllowAnonymous]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
        {
            var token = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.Token == request.RefreshToken);
            if (token != null)
            {
                var sessionId = token.SessionId ?? token.Id;
                var family = await _context.RefreshTokens.Where(candidate => candidate.UserId == token.UserId
                    && (candidate.SessionId ?? candidate.Id) == sessionId && candidate.RevokedAt == null).ToListAsync();
                foreach (var active in family) active.RevokedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "Logged out successfully" });
        }
    }

    public class RegisterRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class RefreshRequest
    {
        public string RefreshToken { get; set; }
    }
}
