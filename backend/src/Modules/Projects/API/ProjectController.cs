using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Modules.Projects.API
{
    [ApiController]
    [Route("api/projects")]
    public class ProjectController : ControllerBase
    {
        private const int MaxSystemPromptLength = 20_000;

        private readonly AppDbContext _context;
        private readonly IAIBehaviorSettingsService _aiBehaviorSettingsService;

        public ProjectController(AppDbContext context, IAIBehaviorSettingsService aiBehaviorSettingsService)
        {
            _context = context;
            _aiBehaviorSettingsService = aiBehaviorSettingsService;
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
                GeminiModel = "gemini-flash-latest",
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
                    settings.IsWhatsAppGroupAutomationEnabled,
                    settings.GroupAutomationManagerPhone,
                    settings.ActiveInstructors,
                    settings.HumanTransferEnabled,
                    settings.HumanTransferPhone,
                    settings.MessengerAiAutoReplyEnabled,
                    settings.MessengerReplyDelay,
                    settings.CommentsAiAutoReplyEnabled,
                    settings.CommentsReplyDelay,
                    settings.SystemPrompt,
                    AiBehavior = _aiBehaviorSettingsService.Resolve(settings)
                } : null
            });
        }

        [HttpGet("{id}/human-transfer-overview")]
        public async Task<IActionResult> GetHumanTransferOverview(Guid id)
        {
            var settings = await _context.ProjectSettings.FirstOrDefaultAsync(s => s.ProjectId == id);
            if (settings == null)
            {
                return NotFound(new { error = "Settings not found for this project" });
            }

            var projectZone = TimezoneHelper.GetTimeZone(settings.Timezone);
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone).Date;
            var utcStartOfToday = TimeZoneInfo.ConvertTimeToUtc(localToday, projectZone);
            var humanTransferRequests = await _context.NotificationAlerts
                .Where(a => a.ProjectId == id && a.Type == "HumanTransferRequest")
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            return Ok(new
            {
                settings.HumanTransferEnabled,
                settings.HumanTransferPhone,
                IsReady = settings.HumanTransferEnabled && !string.IsNullOrWhiteSpace(settings.HumanTransferPhone),
                TotalRequests = humanTransferRequests.Count,
                TodayRequests = humanTransferRequests.Count(a => a.CreatedAt >= utcStartOfToday),
                UnreadRequests = humanTransferRequests.Count(a => !a.IsRead),
                RecentRequests = humanTransferRequests.Take(8).Select(a => new
                {
                    a.Id,
                    a.Message,
                    a.CreatedAt,
                    a.IsRead
                })
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

            var validationErrors = _aiBehaviorSettingsService.Validate(request.AiBehavior);
            if (validationErrors.Count > 0)
            {
                return BadRequest(new { error = string.Join(" ", validationErrors) });
            }

            if (!string.IsNullOrEmpty(request.SystemPrompt) && request.SystemPrompt.Length > MaxSystemPromptLength)
            {
                return BadRequest(new { error = $"systemPrompt exceeds {MaxSystemPromptLength} characters. Keep only short project-specific instructions here; the protected core AI prompt is already built into the backend." });
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
                    IsWhatsAppGroupAutomationEnabled = request.IsWhatsAppGroupAutomationEnabled,
                    GroupAutomationManagerPhone = request.GroupAutomationManagerPhone ?? "+201068690092",
                    ActiveInstructors = request.ActiveInstructors ?? string.Empty,
                    HumanTransferEnabled = request.HumanTransferEnabled,
                    HumanTransferPhone = request.HumanTransferPhone,
                    SystemPrompt = request.SystemPrompt,
                    AiBehaviorSettingsJson = request.AiBehavior != null ? _aiBehaviorSettingsService.Serialize(request.AiBehavior) : null,
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
                settings.IsWhatsAppGroupAutomationEnabled = request.IsWhatsAppGroupAutomationEnabled;
                settings.GroupAutomationManagerPhone = request.GroupAutomationManagerPhone ?? "+201068690092";
                if (request.ActiveInstructors != null) settings.ActiveInstructors = request.ActiveInstructors;
                settings.HumanTransferEnabled = request.HumanTransferEnabled;
                settings.HumanTransferPhone = request.HumanTransferPhone;
                settings.MessengerAiAutoReplyEnabled = request.MessengerAiAutoReplyEnabled;
                if (request.MessengerReplyDelay.HasValue) settings.MessengerReplyDelay = request.MessengerReplyDelay.Value;
                settings.CommentsAiAutoReplyEnabled = request.CommentsAiAutoReplyEnabled;
                if (request.CommentsReplyDelay.HasValue) settings.CommentsReplyDelay = request.CommentsReplyDelay.Value;
                settings.SystemPrompt = request.SystemPrompt;
                if (request.AiBehavior != null)
                {
                    settings.AiBehaviorSettingsJson = _aiBehaviorSettingsService.Serialize(request.AiBehavior);
                }
                settings.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Settings updated successfully" });
        }

        private static string NormalizeGeminiModel(string? model)
        {
            return model switch
            {
                "gemini-flash-latest" => "gemini-flash-latest",
                "gemini-flash-lite-latest" => "gemini-flash-lite-latest",
                "gemini-3.6-flash" => "gemini-3.6-flash",
                "gemini-3.5-flash-lite" => "gemini-3.5-flash-lite",
                "gemini-2.5-flash-lite" => "gemini-2.5-flash-lite",
                "gemini-3.1-flash-lite" => "gemini-3.1-flash-lite",
                "gemini-3.5-flash" => "gemini-3.5-flash",
                _ => "gemini-flash-latest"
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
        public bool IsWhatsAppGroupAutomationEnabled { get; set; }
        public string? GroupAutomationManagerPhone { get; set; }
        public string? ActiveInstructors { get; set; }
        public bool HumanTransferEnabled { get; set; }
        public string? HumanTransferPhone { get; set; }
        public bool MessengerAiAutoReplyEnabled { get; set; }
        public int? MessengerReplyDelay { get; set; }
        public bool CommentsAiAutoReplyEnabled { get; set; }
        public int? CommentsReplyDelay { get; set; }
        public string? SystemPrompt { get; set; }
        public AIBehaviorSettings? AiBehavior { get; set; }
    }
}
