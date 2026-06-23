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
                Timezone = "Africa/Cairo",
                GeminiModel = "gemini-3.5-flash",
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
                        Timezone = "Africa/Cairo"
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
                    settings.GeminiModel,
                    settings.AiTonePreference,
                    settings.AiTargetAudience,
                    settings.ReplyDelay,
                    settings.MaxDailyMessages,
                    settings.IsGroupAppointmentsEnabled,
                    settings.MessengerAiAutoReplyEnabled,
                    settings.MessengerReplyDelay,
                    settings.CommentsAiAutoReplyEnabled,
                    settings.CommentsReplyDelay,
                    settings.SystemPrompt
                } : null
            });
        }

        [HttpPut("{id}/settings")]
        public async Task<IActionResult> UpdateSettings(Guid id, [FromBody] UpdateSettingsRequest request)
        {
            var project = await _context.Projects.FindAsync(id);
            if (project != null && !string.IsNullOrWhiteSpace(request.ProjectName))
            {
                project.Name = request.ProjectName;
                project.UpdatedAt = DateTime.UtcNow;
            }

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
                    Timezone = request.Timezone ?? "Africa/Cairo",
                    GeminiApiKey = request.GeminiApiKey ?? string.Empty,
                    GeminiModel = NormalizeGeminiModel(request.GeminiModel),
                    AiTonePreference = request.AiTonePreference ?? "العامية المصرية الروشة والصايعة",
                    AiTargetAudience = request.AiTargetAudience ?? "طلاب كورس كول سنتر يبحثون عن عمل",
                    ReplyDelay = request.ReplyDelay ?? 3,
                    MaxDailyMessages = request.MaxDailyMessages ?? 500,
                    IsGroupAppointmentsEnabled = request.IsGroupAppointmentsEnabled,
                    SystemPrompt = request.SystemPrompt,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.ProjectSettings.Add(settings);
            }
            else
            {
                settings.AiAutoReplyEnabled = request.AiAutoReplyEnabled;
                settings.Timezone = request.Timezone ?? "Africa/Cairo";
                settings.GeminiApiKey = request.GeminiApiKey ?? string.Empty;
                settings.GeminiModel = NormalizeGeminiModel(request.GeminiModel);
                settings.AiTonePreference = request.AiTonePreference ?? "العامية المصرية الروشة والصايعة";
                settings.AiTargetAudience = request.AiTargetAudience ?? "طلاب كورس كول سنتر يبحثون عن عمل";
                if (request.ReplyDelay.HasValue) settings.ReplyDelay = request.ReplyDelay.Value;
                if (request.MaxDailyMessages.HasValue) settings.MaxDailyMessages = request.MaxDailyMessages.Value;
                settings.IsGroupAppointmentsEnabled = request.IsGroupAppointmentsEnabled;
                settings.MessengerAiAutoReplyEnabled = request.MessengerAiAutoReplyEnabled;
                if (request.MessengerReplyDelay.HasValue) settings.MessengerReplyDelay = request.MessengerReplyDelay.Value;
                settings.CommentsAiAutoReplyEnabled = request.CommentsAiAutoReplyEnabled;
                if (request.CommentsReplyDelay.HasValue) settings.CommentsReplyDelay = request.CommentsReplyDelay.Value;
                settings.SystemPrompt = request.SystemPrompt;
                settings.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Settings updated successfully" });
        }

        private static string NormalizeGeminiModel(string? model)
        {
            return model switch
            {
                "gemini-2.5-flash-lite" => "gemini-2.5-flash-lite",
                "gemini-3.1-flash-lite" => "gemini-3.1-flash-lite",
                "gemini-3.5-flash" => "gemini-3.5-flash",
                _ => "gemini-3.5-flash"
            };
        }
    }

    public class CreateProjectRequest
    {
        public string Name { get; set; }
    }

    public class UpdateSettingsRequest
    {
        public string? ProjectName { get; set; }
        public bool AiAutoReplyEnabled { get; set; }
        public string? Timezone { get; set; }
        public string? GeminiApiKey { get; set; }
        public string? GeminiModel { get; set; }
        public string? AiTonePreference { get; set; }
        public string? AiTargetAudience { get; set; }
        public int? ReplyDelay { get; set; }
        public int? MaxDailyMessages { get; set; }
        public bool IsGroupAppointmentsEnabled { get; set; }
        public bool MessengerAiAutoReplyEnabled { get; set; }
        public int? MessengerReplyDelay { get; set; }
        public bool CommentsAiAutoReplyEnabled { get; set; }
        public int? CommentsReplyDelay { get; set; }
        public string? SystemPrompt { get; set; }
    }
}
