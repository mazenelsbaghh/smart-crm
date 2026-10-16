using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Auth.Domain;
using Modules.Auth.Services;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.Auth.API;

[ApiController]
[Authorize]
[ProjectPermission("read")]
[Route("api/auth/projects")]
public sealed class ProjectSessionsController(
    AppDbContext db, IPasswordHasher passwordHasher, IJwtService jwtService) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var account = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(user => user.Id == UserId);
        if (account is null) return Unauthorized();
        var memberships = db.ProjectMemberships.Where(membership => membership.UserId == account.Id);
        var projects = await db.Projects.IgnoreQueryFilters()
            .Where(project => project.Id == account.ProjectId || memberships.Any(membership => membership.ProjectId == project.Id))
            .OrderByDescending(project => project.Id == account.ProjectId).ThenBy(project => project.Name)
            .Select(project => new { project.Id, project.Name }).ToListAsync();
        return Ok(projects);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateProjectRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return BadRequest(new { error = "اكتب اسم المشروع من حرف إلى 120 حرفًا." });
        var account = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(user => user.Id == UserId);
        if (account is null) return Unauthorized();
        if (!Guid.TryParse(User.FindFirstValue("ProjectId"), out var activeProjectId)) return Forbid();
        var role = await ProjectAccess.RoleFor(db, account, activeProjectId);
        if (role is not ("Owner" or "Admin")) return Forbid();

        var project = new Project { Name = name };
        db.Projects.Add(project);
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = project.Id, Timezone = "Africa/Cairo" });
        db.ProjectMemberships.Add(new ProjectMembership { UserId = account.Id, ProjectId = project.Id, Role = "Owner" });
        await db.SaveChangesAsync();
        return Ok(new { projectId = project.Id, project.Name });
    }

    [HttpPost("link")]
    public async Task<IActionResult> Link(LoginRequest request)
    {
        var owner = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(user => user.Email == request.Email);
        if (owner is null || !passwordHasher.VerifyPassword(request.Password, owner.PasswordHash) || owner.Role != "Owner")
            return BadRequest(new { error = "تعذر ربط المشروع. تأكد من بريد وكلمة مرور مالك المشروع." });
        var account = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(user => user.Id == UserId);
        if (account is null) return Unauthorized();
        if (await ProjectAccess.RoleFor(db, account, owner.ProjectId) is not null)
            return Ok(new { projectId = owner.ProjectId });
        db.ProjectMemberships.Add(new ProjectMembership { UserId = account.Id, ProjectId = owner.ProjectId, Role = "Owner" });
        await db.SaveChangesAsync();
        return Ok(new { projectId = owner.ProjectId });
    }

    [HttpPost("switch")]
    public async Task<IActionResult> Switch(SwitchProjectRequest request)
    {
        var account = await db.Users.IgnoreQueryFilters().SingleOrDefaultAsync(user => user.Id == UserId);
        if (account is null) return Unauthorized();
        var role = await ProjectAccess.RoleFor(db, account, request.ProjectId);
        if (role is null || !await db.Projects.IgnoreQueryFilters().AnyAsync(project => project.Id == request.ProjectId))
            return Forbid();
        var previousToken = await db.RefreshTokens.SingleOrDefaultAsync(token => token.Token == request.RefreshToken && token.UserId == account.Id);
        if (previousToken is null || !previousToken.IsActive) return Unauthorized();
        previousToken.RevokedAt = DateTime.UtcNow;
        var refreshToken = new RefreshToken
        {
            UserId = account.Id, ProjectId = request.ProjectId,
            Token = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddDays(7)
        };
        db.RefreshTokens.Add(refreshToken);
        refreshToken.SessionId = refreshToken.Id;
        await db.SaveChangesAsync();
        return Ok(new
        {
            accessToken = jwtService.GenerateToken(new(account.Id, account.Email, role, request.ProjectId, refreshToken.Id)),
            refreshToken = refreshToken.Token,
            user = new { account.Id, account.Email, Role = role }
        });
    }
}

public sealed record SwitchProjectRequest(Guid ProjectId, string RefreshToken);

public sealed record CreateProjectRequest(string Name);
