using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using System;
using System.Threading.Tasks;

namespace Modules.Projects.API
{
    [ApiController]
    [Route("api/projects")]
    public class ProjectController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ProjectController(AppDbContext context)
        {
            _context = context;
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateProjectRequest request)
        {
            var project = new Project
            {
                Name = request.Name
            };

            _context.Projects.Add(project);
            await _context.SaveChangesAsync();

            // Create default settings for this project
            var settings = new ProjectSettings
            {
                ProjectId = project.Id,
                AiAutoReplyEnabled = false,
                Timezone = "UTC",
                ReplyDelay = 3,
                MaxDailyMessages = 500
            };

            _context.ProjectSettings.Add(settings);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(Get), new { id = project.Id }, project);
        }

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var projects = await _context.Projects.ToListAsync();
            return Ok(projects);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project == null)
            {
                return NotFound(new { error = "Project not found" });
            }

            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == id);
            if (settings == null)
            {
                var settingsExistsAtAll = await _context.ProjectSettings.IgnoreQueryFilters().AnyAsync(s => s.ProjectId == id);
                if (!settingsExistsAtAll)
                {
                    settings = new ProjectSettings
                    {
                        ProjectId = id,
                        AiAutoReplyEnabled = false,
                        Timezone = "UTC"
                    };
                    _context.ProjectSettings.Add(settings);
                    await _context.SaveChangesAsync();
                }
            }

            return Ok(new
            {
                project.Id,
                project.Name,
                project.CreatedAt,
                settings = settings != null ? new {
                    settings.AiAutoReplyEnabled,
                    settings.Timezone,
                    settings.GeminiApiKey,
                    settings.AiTonePreference,
                    settings.AiTargetAudience,
                    settings.ReplyDelay,
                    settings.MaxDailyMessages
                } : null
            });
        }

        [HttpPut("{id}/settings")]
        public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] UpdateSettingsRequest request)
        {
            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == id);
            if (settings == null)
            {
                var settingsExistsAtAll = await _context.ProjectSettings.IgnoreQueryFilters().AnyAsync(s => s.ProjectId == id);
                if (settingsExistsAtAll)
                {
                    return NotFound(new { error = "Settings not found for this project" });
                }

                settings = new ProjectSettings
                {
                    ProjectId = id,
                    AiAutoReplyEnabled = request.AiAutoReplyEnabled,
                    Timezone = request.Timezone ?? "UTC",
                    GeminiApiKey = request.GeminiApiKey ?? string.Empty,
                    AiTonePreference = request.AiTonePreference ?? "العامية المصرية الروشة والصايعة",
                    AiTargetAudience = request.AiTargetAudience ?? "طلاب كورس كول سنتر يبحثون عن عمل",
                    ReplyDelay = request.ReplyDelay ?? 3,
                    MaxDailyMessages = request.MaxDailyMessages ?? 500,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.ProjectSettings.Add(settings);
            }
            else
            {
                settings.AiAutoReplyEnabled = request.AiAutoReplyEnabled;
                settings.Timezone = request.Timezone ?? "UTC";
                settings.GeminiApiKey = request.GeminiApiKey ?? string.Empty;
                settings.AiTonePreference = request.AiTonePreference ?? "العامية المصرية الروشة والصايعة";
                settings.AiTargetAudience = request.AiTargetAudience ?? "طلاب كورس كول سنتر يبحثون عن عمل";
                if (request.ReplyDelay.HasValue) settings.ReplyDelay = request.ReplyDelay.Value;
                if (request.MaxDailyMessages.HasValue) settings.MaxDailyMessages = request.MaxDailyMessages.Value;
                settings.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Settings updated successfully" });
        }
    }

    public class CreateProjectRequest
    {
        public string Name { get; set; }
    }

    public class UpdateSettingsRequest
    {
        public bool AiAutoReplyEnabled { get; set; }
        public string? Timezone { get; set; }
        public string? GeminiApiKey { get; set; }
        public string? AiTonePreference { get; set; }
        public string? AiTargetAudience { get; set; }
        public int? ReplyDelay { get; set; }
        public int? MaxDailyMessages { get; set; }
    }
}
