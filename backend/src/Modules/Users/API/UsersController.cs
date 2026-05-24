using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Domain;
using Shared.Infrastructure;
using Shared.Security;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.Users.API
{
    [ApiController]
    [Route("api")]
    public class UsersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IPasswordHasher _passwordHasher;

        private static readonly string[] ValidRoles = new[]
        {
            "Owner", "Admin", "Supervisor", "Agent", "AI Reviewer", "Analyst"
        };

        public UsersController(AppDbContext context, IPasswordHasher passwordHasher)
        {
            _context = context;
            _passwordHasher = passwordHasher;
        }

        [HttpGet("projects/{projectId}/users")]
        public async Task<IActionResult> ListUsers(Guid projectId)
        {
            var users = await _context.Users
                .Where(u => u.ProjectId == projectId)
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Email = u.Email,
                    Role = u.Role,
                    ProjectId = u.ProjectId,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt
                })
                .ToListAsync();

            return Ok(users);
        }

        [HttpPost("projects/{projectId}/users/invite")]
        public async Task<IActionResult> InviteUser(Guid projectId, [FromBody] InviteUserRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { error = "Email is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { error = "Password is required" });
            }

            var role = request.Role ?? "Agent";
            if (!ValidRoles.Contains(role))
            {
                return BadRequest(new { error = $"Invalid role. Allowed roles are: {string.Join(", ", ValidRoles)}" });
            }

            if (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == request.Email))
            {
                return BadRequest(new { error = "Email already exists" });
            }

            var user = new User
            {
                Email = request.Email,
                PasswordHash = _passwordHasher.HashPassword(request.Password),
                Role = role,
                ProjectId = projectId
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role,
                ProjectId = user.ProjectId,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpGet("users/{id}")]
        public async Task<IActionResult> GetUser(Guid id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            return Ok(new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role,
                ProjectId = user.ProjectId,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpPut("users/{id}")]
        public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                if (request.Email != user.Email && await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == request.Email))
                {
                    return BadRequest(new { error = "Email already exists" });
                }
                user.Email = request.Email;
            }

            if (!string.IsNullOrWhiteSpace(request.Role))
            {
                if (!ValidRoles.Contains(request.Role))
                {
                    return BadRequest(new { error = $"Invalid role. Allowed roles are: {string.Join(", ", ValidRoles)}" });
                }
                user.Role = request.Role;
            }

            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role,
                ProjectId = user.ProjectId,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpPut("users/{id}/role")]
        public async Task<IActionResult> UpdateUserRole(Guid id, [FromBody] UpdateUserRoleRequest request)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            if (string.IsNullOrWhiteSpace(request.Role) || !ValidRoles.Contains(request.Role))
            {
                return BadRequest(new { error = $"Invalid role. Allowed roles are: {string.Join(", ", ValidRoles)}" });
            }

            user.Role = request.Role;
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new UserDto
            {
                Id = user.Id,
                Email = user.Email,
                Role = user.Role,
                ProjectId = user.ProjectId,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            });
        }

        [HttpDelete("users/{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "User deleted successfully" });
        }
    }

    public class InviteUserRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string Role { get; set; }
    }

    public class UpdateUserRequest
    {
        public string Email { get; set; }
        public string Role { get; set; }
    }

    public class UpdateUserRoleRequest
    {
        public string Role { get; set; }
    }

    public class UserDto
    {
        public Guid Id { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public Guid ProjectId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
