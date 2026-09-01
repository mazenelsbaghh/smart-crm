using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.AI.Services;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Shared.Queue;
using Shared.Security;

namespace Modules.Projects.API
{
    [ApiController]
    [Authorize]
    [Route("api/projects")]
    public class ProjectController : ControllerBase
    {
        private const int MaxSystemPromptLength = 20_000;
        private static readonly Regex GoogleCloudProjectIdPattern = new(
            "^[a-z][a-z0-9-]{4,28}[a-z0-9]$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        private static readonly HashSet<string> SupportedGeminiModels = new(StringComparer.Ordinal)
        {
            "gemini-flash-latest",
            "gemini-flash-lite-latest",
            "gemini-3.6-flash",
            "gemini-3.5-flash-lite",
            "gemini-2.5-flash-lite",
            "gemini-3.1-flash-lite",
            "gemini-3.5-flash"
        };
        private static readonly HashSet<string> SupportedCustomerReplyProviders = new(StringComparer.Ordinal)
        {
            CustomerReplyProviders.Gemini,
            CustomerReplyProviders.OpenAI,
            CustomerReplyProviders.Xai
        };
        private static readonly HashSet<string> SupportedOpenAiCustomerReplyModels = new(StringComparer.Ordinal)
        {
            "gpt-5.6",
            "gpt-5.6-terra",
            "gpt-5.6-luna"
        };
        private static readonly HashSet<string> SupportedXaiCustomerReplyModels = new(StringComparer.Ordinal)
        {
            "grok-4.6",
            "grok-4.3"
        };

        private readonly AppDbContext _context;
        private readonly IAIBehaviorSettingsService _aiBehaviorSettingsService;
        private readonly IProjectAuthorizationService _authorization;
        private readonly IProjectSecretVault _secretVault;

        public ProjectController(
            AppDbContext context,
            IAIBehaviorSettingsService aiBehaviorSettingsService,
            IProjectAuthorizationService authorization,
            IProjectSecretVault secretVault)
        {
            _context = context;
            _aiBehaviorSettingsService = aiBehaviorSettingsService;
            _authorization = authorization;
            _secretVault = secretVault;
        }

        [HttpPost]
        public IActionResult Create() => StatusCode(StatusCodes.Status403Forbidden, new
        {
            code = "PROJECT_CREATION_DISABLED",
            error = "Project creation requires an administrator-managed onboarding flow."
        });

        [HttpGet]
        public async Task<IActionResult> List()
        {
            var projectId = _authorization.GetProjectId(User);
            if (projectId is null || !_authorization.CanRead(User, projectId.Value)) return Forbid();
            var projects = await _context.Projects.IgnoreQueryFilters()
                .Where(project => project.Id == projectId.Value)
                .ToListAsync();
            return Ok(projects);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> Get(Guid id)
        {
            if (!_authorization.CanRead(User, id)) return Forbid();
            var project = await _context.Projects.IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.Id == id);
            if (project == null)
            {
                return NotFound(new { error = "Project not found" });
            }

            var settings = await _context.ProjectSettings.IgnoreQueryFilters()
                .SingleOrDefaultAsync(s => s.ProjectId == id);
            var now = DateTime.UtcNow;

            return Ok(new
            {
                project.Id,
                project.Name,
                project.CreatedAt,
                settings = settings != null ? new {
                    settings.AiAutoReplyEnabled,
                    settings.Timezone,
                    GeminiApiKeyConfigured = !string.IsNullOrWhiteSpace(settings.GeminiApiKey),
                    GeminiAgentPlatformApiKeyConfigured =
                        !string.IsNullOrWhiteSpace(settings.GeminiAgentPlatformApiKey),
                    settings.GeminiModel,
                    TemporaryGeminiModel = settings.HasActiveTemporaryGeminiModel(now)
                        ? settings.TemporaryGeminiModel
                        : null,
                    TemporaryGeminiModelExpiresAtUtc = settings.HasActiveTemporaryGeminiModel(now)
                        ? settings.TemporaryGeminiModelExpiresAtUtc
                        : null,
                    EffectiveGeminiModel = settings.ResolveGeminiModel(now),
                    settings.GeminiEnterpriseProjectId,
                    settings.CustomerReplyProvider,
                    CustomerReplyOpenAiApiKeyConfigured = !string.IsNullOrWhiteSpace(settings.CustomerReplyOpenAiApiKey),
                    CustomerReplyXaiApiKeyConfigured = !string.IsNullOrWhiteSpace(settings.CustomerReplyXaiApiKey),
                    settings.CustomerReplyModel,
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
                    settings.IsTalkTipsTrialGateEnabled,
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
            if (!_authorization.CanRead(User, id)) return Forbid();
            var settings = await _context.ProjectSettings.IgnoreQueryFilters()
                .SingleOrDefaultAsync(s => s.ProjectId == id);
            if (settings == null)
            {
                return NotFound(new { error = "Settings not found for this project" });
            }

            var projectZone = TimezoneHelper.GetTimeZone(settings.Timezone);
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, projectZone).Date;
            var utcStartOfToday = TimeZoneInfo.ConvertTimeToUtc(localToday, projectZone);
            var humanTransferRequests = await _context.NotificationAlerts.IgnoreQueryFilters()
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
            if (!_authorization.CanManageProject(User, id)) return Forbid();
            var project = await _context.Projects.IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.Id == id);
            if (project is null) return NotFound(new { error = "Project not found" });
            if (!string.IsNullOrWhiteSpace(request.ProjectName))
            {
                project.Name = request.ProjectName.Trim();
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

            if (request.GeminiModel is not null && !SupportedGeminiModels.Contains(request.GeminiModel))
            {
                return BadRequest(new { error = "Unsupported Gemini model" });
            }

            if (!string.IsNullOrWhiteSpace(request.GeminiEnterpriseProjectId)
                && !IsValidGoogleCloudProjectId(request.GeminiEnterpriseProjectId.Trim()))
            {
                return BadRequest(new { error = "Invalid Google Cloud project ID" });
            }

            if (request.CustomerReplyProvider is not null &&
                !SupportedCustomerReplyProviders.Contains(request.CustomerReplyProvider))
            {
                return BadRequest(new { error = "Unsupported customer reply provider" });
            }

            if (request.Timezone is not null && !IsValidTimezone(request.Timezone))
            {
                return BadRequest(new { error = "Invalid IANA timezone" });
            }

            var settings = await _context.ProjectSettings.IgnoreQueryFilters()
                .SingleOrDefaultAsync(s => s.ProjectId == id);
            var requestedCustomerReplyProvider = request.CustomerReplyProvider
                ?? settings?.CustomerReplyProvider
                ?? CustomerReplyProviders.Gemini;
            var providerChanged = settings is not null &&
                request.CustomerReplyProvider is not null &&
                !string.Equals(request.CustomerReplyProvider, settings.CustomerReplyProvider, StringComparison.Ordinal);
            var requestedCustomerReplyModel = request.CustomerReplyModel
                ?? (providerChanged ? null : settings?.CustomerReplyModel)
                ?? DefaultCustomerReplyModel(requestedCustomerReplyProvider);
            if (!IsSupportedCustomerReplyModel(requestedCustomerReplyProvider, requestedCustomerReplyModel))
            {
                return BadRequest(new { error = $"Unsupported {requestedCustomerReplyProvider} customer reply model" });
            }

            var openAiKeyWillBeConfigured = !request.ClearCustomerReplyOpenAiApiKey &&
                (!string.IsNullOrWhiteSpace(request.CustomerReplyOpenAiApiKey) ||
                 !string.IsNullOrWhiteSpace(settings?.CustomerReplyOpenAiApiKey));
            if (requestedCustomerReplyProvider == CustomerReplyProviders.OpenAI && !openAiKeyWillBeConfigured)
            {
                return BadRequest(new { error = "OpenAI API key is required before selecting OpenAI for customer replies" });
            }
            var xaiKeyWillBeConfigured = !request.ClearCustomerReplyXaiApiKey &&
                (!string.IsNullOrWhiteSpace(request.CustomerReplyXaiApiKey) ||
                 !string.IsNullOrWhiteSpace(settings?.CustomerReplyXaiApiKey));
            if (requestedCustomerReplyProvider == CustomerReplyProviders.Xai && !xaiKeyWillBeConfigured)
            {
                return BadRequest(new { error = "xAI API key is required before selecting xAI for customer replies" });
            }

            if (settings == null)
            {
                if (request.Timezone is null)
                    return BadRequest(new { error = "A valid IANA timezone must be selected before saving" });
                settings = new ProjectSettings
                {
                    ProjectId = id,
                    AiAutoReplyEnabled = request.AiAutoReplyEnabled,
                    Timezone = request.Timezone,
                    GeminiApiKey = request.ClearGeminiApiKey
                        ? string.Empty
                        : string.IsNullOrWhiteSpace(request.GeminiApiKey)
                            ? string.Empty
                            : _secretVault.Protect(id, request.GeminiApiKey),
                    GeminiAgentPlatformApiKey = request.ClearGeminiAgentPlatformApiKey
                        ? string.Empty
                        : string.IsNullOrWhiteSpace(request.GeminiAgentPlatformApiKey)
                            ? string.Empty
                            : _secretVault.Protect(id, request.GeminiAgentPlatformApiKey),
                    GeminiModel = request.GeminiModel ?? "gemini-3.5-flash",
                    GeminiEnterpriseProjectId = NormalizeGoogleCloudProjectId(request.GeminiEnterpriseProjectId),
                    CustomerReplyProvider = requestedCustomerReplyProvider,
                    CustomerReplyOpenAiApiKey = request.ClearCustomerReplyOpenAiApiKey
                        ? string.Empty
                        : string.IsNullOrWhiteSpace(request.CustomerReplyOpenAiApiKey)
                            ? string.Empty
                            : _secretVault.Protect(id, request.CustomerReplyOpenAiApiKey),
                    CustomerReplyXaiApiKey = request.ClearCustomerReplyXaiApiKey
                        ? string.Empty
                        : string.IsNullOrWhiteSpace(request.CustomerReplyXaiApiKey)
                            ? string.Empty
                            : _secretVault.Protect(id, request.CustomerReplyXaiApiKey),
                    CustomerReplyModel = requestedCustomerReplyModel,
                    AiTonePreference = request.AiTonePreference ?? "العامية المصرية المهذبة والمحترمة",
                    AiTargetAudience = request.AiTargetAudience ?? string.Empty,
                    ReplyDelay = request.ReplyDelay ?? 3,
                    MaxDailyMessages = request.MaxDailyMessages ?? 500,
                    IsGroupAppointmentsEnabled = request.IsGroupAppointmentsEnabled,
                    IsWhatsAppGroupAutomationEnabled = request.IsWhatsAppGroupAutomationEnabled,
                    GroupAutomationManagerPhone = request.GroupAutomationManagerPhone?.Trim() ?? string.Empty,
                    ActiveInstructors = request.ActiveInstructors ?? string.Empty,
                    HumanTransferEnabled = request.HumanTransferEnabled,
                    HumanTransferPhone = request.HumanTransferPhone,
                    IsTalkTipsTrialGateEnabled = request.IsTalkTipsTrialGateEnabled,
                    MessengerAiAutoReplyEnabled = request.MessengerAiAutoReplyEnabled,
                    MessengerReplyDelay = request.MessengerReplyDelay ?? 5,
                    CommentsAiAutoReplyEnabled = request.CommentsAiAutoReplyEnabled,
                    CommentsReplyDelay = request.CommentsReplyDelay ?? 10,
                    SystemPrompt = request.SystemPrompt,
                    AiBehaviorSettingsJson = request.AiBehavior != null ? _aiBehaviorSettingsService.Serialize(request.AiBehavior) : null,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.ProjectSettings.Add(settings);
            }
            else
            {
                settings.AiAutoReplyEnabled = request.AiAutoReplyEnabled;
                if (request.Timezone is not null) settings.Timezone = request.Timezone;
                if (request.ClearGeminiApiKey)
                {
                    settings.GeminiApiKey = string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(request.GeminiApiKey))
                {
                    settings.GeminiApiKey = _secretVault.Protect(id, request.GeminiApiKey);
                }
                if (request.ClearGeminiAgentPlatformApiKey)
                {
                    settings.GeminiAgentPlatformApiKey = string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(request.GeminiAgentPlatformApiKey))
                {
                    settings.GeminiAgentPlatformApiKey = _secretVault.Protect(
                        id,
                        request.GeminiAgentPlatformApiKey);
                }
                if (request.GeminiModel is not null) settings.GeminiModel = request.GeminiModel;
                if (request.GeminiEnterpriseProjectId is not null)
                {
                    settings.GeminiEnterpriseProjectId = NormalizeGoogleCloudProjectId(request.GeminiEnterpriseProjectId);
                }
                if (request.CustomerReplyProvider is not null)
                {
                    settings.CustomerReplyProvider = request.CustomerReplyProvider;
                }
                if (request.ClearCustomerReplyOpenAiApiKey)
                {
                    settings.CustomerReplyOpenAiApiKey = string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(request.CustomerReplyOpenAiApiKey))
                {
                    settings.CustomerReplyOpenAiApiKey = _secretVault.Protect(id, request.CustomerReplyOpenAiApiKey);
                }
                if (request.ClearCustomerReplyXaiApiKey)
                {
                    settings.CustomerReplyXaiApiKey = string.Empty;
                }
                else if (!string.IsNullOrWhiteSpace(request.CustomerReplyXaiApiKey))
                {
                    settings.CustomerReplyXaiApiKey = _secretVault.Protect(id, request.CustomerReplyXaiApiKey);
                }
                if (request.CustomerReplyModel is not null || providerChanged)
                {
                    settings.CustomerReplyModel = requestedCustomerReplyModel;
                }
                settings.AiTonePreference = request.AiTonePreference ?? "العامية المصرية المهذبة والمحترمة";
                settings.AiTargetAudience = request.AiTargetAudience ?? string.Empty;
                if (request.ReplyDelay.HasValue) settings.ReplyDelay = request.ReplyDelay.Value;
                if (request.MaxDailyMessages.HasValue) settings.MaxDailyMessages = request.MaxDailyMessages.Value;
                settings.IsGroupAppointmentsEnabled = request.IsGroupAppointmentsEnabled;
                settings.IsWhatsAppGroupAutomationEnabled = request.IsWhatsAppGroupAutomationEnabled;
                settings.GroupAutomationManagerPhone = request.GroupAutomationManagerPhone?.Trim() ?? string.Empty;
                if (request.ActiveInstructors != null) settings.ActiveInstructors = request.ActiveInstructors;
                settings.HumanTransferEnabled = request.HumanTransferEnabled;
                settings.HumanTransferPhone = request.HumanTransferPhone;
                settings.IsTalkTipsTrialGateEnabled = request.IsTalkTipsTrialGateEnabled;
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

            if (!IsValidTimezone(settings.Timezone))
                return BadRequest(new { error = "A valid IANA timezone must be selected before saving" });

            settings.AdvertisingContextVersion++;
            EnqueueAdvertisingContext(id, settings);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Settings updated successfully" });
        }

        [HttpPut("{id}/settings/gemini-model-override")]
        public async Task<IActionResult> ActivateGeminiModelOverride(
            Guid id,
            [FromBody] TemporaryGeminiModelRequest request)
        {
            if (!_authorization.CanManageProject(User, id)) return Forbid();
            if (!SupportedGeminiModels.Contains(request.Model))
                return BadRequest(new { error = "Unsupported Gemini model" });
            if (request.DurationMinutes is < 15 or > 10_080)
                return BadRequest(new { error = "Temporary model duration must be between 15 minutes and 7 days" });

            var settings = await _context.ProjectSettings.IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.ProjectId == id);
            if (settings is null) return NotFound(new { error = "Project settings not found" });

            var now = DateTime.UtcNow;
            settings.TemporaryGeminiModel = request.Model;
            settings.TemporaryGeminiModelExpiresAtUtc = now.AddMinutes(request.DurationMinutes);
            settings.UpdatedAt = now;
            settings.AdvertisingContextVersion++;
            EnqueueAdvertisingContext(id, settings);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                effectiveGeminiModel = settings.ResolveGeminiModel(now),
                temporaryGeminiModelExpiresAtUtc = settings.TemporaryGeminiModelExpiresAtUtc,
                baseGeminiModel = settings.GeminiModel
            });
        }

        [HttpDelete("{id}/settings/gemini-model-override")]
        public async Task<IActionResult> CancelGeminiModelOverride(Guid id)
        {
            if (!_authorization.CanManageProject(User, id)) return Forbid();
            var settings = await _context.ProjectSettings.IgnoreQueryFilters()
                .SingleOrDefaultAsync(candidate => candidate.ProjectId == id);
            if (settings is null) return NotFound(new { error = "Project settings not found" });

            settings.TemporaryGeminiModel = null;
            settings.TemporaryGeminiModelExpiresAtUtc = null;
            settings.UpdatedAt = DateTime.UtcNow;
            settings.AdvertisingContextVersion++;
            EnqueueAdvertisingContext(id, settings);
            await _context.SaveChangesAsync();
            return Ok(new { effectiveGeminiModel = settings.GeminiModel });
        }

        private void EnqueueAdvertisingContext(Guid projectId, ProjectSettings settings)
        {
            var version = Math.Max(1, settings.AdvertisingContextVersion);
            var effectiveModel = settings.ResolveGeminiModel(DateTime.UtcNow);
            IntegrationOutbox.Enqueue(_context, new ProjectAdvertisingContextChanged
            {
                ProjectId = projectId,
                LifecycleState = "Active",
                ReportingTimezoneIana = settings.Timezone,
                AiConfigurationVersion = version,
                SourceAggregateType = nameof(ProjectSettings),
                SourceAggregateId = projectId,
                SourceVersion = version
            });
            IntegrationOutbox.Enqueue(_context, new ProjectAiConfigurationChanged
            {
                ProjectId = projectId,
                ConfigurationVersion = version,
                AllowedModel = effectiveModel,
                SettingsHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{effectiveModel}:{settings.AiBehaviorSettingsJson}"))).ToLowerInvariant(),
                SourceAggregateType = nameof(ProjectSettings),
                SourceAggregateId = projectId,
                SourceVersion = version
            });
        }

        private static bool IsValidTimezone(string timezone)
        {
            if (string.IsNullOrWhiteSpace(timezone)) return false;
            try
            {
                _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                return true;
            }
            catch (TimeZoneNotFoundException) { return false; }
            catch (InvalidTimeZoneException) { return false; }
        }

        private static bool IsValidGoogleCloudProjectId(string projectId) =>
            GoogleCloudProjectIdPattern.IsMatch(projectId);

        private static string? NormalizeGoogleCloudProjectId(string? projectId) =>
            string.IsNullOrWhiteSpace(projectId) ? null : projectId.Trim();

        private static string DefaultCustomerReplyModel(string provider) => provider switch
        {
            CustomerReplyProviders.Xai => "grok-4.6",
            _ => "gpt-5.6"
        };

        private static bool IsSupportedCustomerReplyModel(string provider, string model) => provider switch
        {
            CustomerReplyProviders.OpenAI => SupportedOpenAiCustomerReplyModels.Contains(model),
            CustomerReplyProviders.Xai => SupportedXaiCustomerReplyModels.Contains(model),
            _ => true
        };
    }

    public class UpdateSettingsRequest
    {
        public string? ProjectName { get; set; }
        public bool AiAutoReplyEnabled { get; set; }
        public string? Timezone { get; set; }
        public string? GeminiApiKey { get; set; }
        public bool ClearGeminiApiKey { get; set; }
        public string? GeminiAgentPlatformApiKey { get; set; }
        public bool ClearGeminiAgentPlatformApiKey { get; set; }
        public string? GeminiModel { get; set; }
        public string? GeminiEnterpriseProjectId { get; set; }
        public string? CustomerReplyProvider { get; set; }
        public string? CustomerReplyOpenAiApiKey { get; set; }
        public bool ClearCustomerReplyOpenAiApiKey { get; set; }
        public string? CustomerReplyXaiApiKey { get; set; }
        public bool ClearCustomerReplyXaiApiKey { get; set; }
        public string? CustomerReplyModel { get; set; }
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
        public bool IsTalkTipsTrialGateEnabled { get; set; }
        public bool MessengerAiAutoReplyEnabled { get; set; }
        public int? MessengerReplyDelay { get; set; }
        public bool CommentsAiAutoReplyEnabled { get; set; }
        public int? CommentsReplyDelay { get; set; }
        public string? SystemPrompt { get; set; }
        public AIBehaviorSettings? AiBehavior { get; set; }
    }

    public sealed class TemporaryGeminiModelRequest
    {
        public string Model { get; set; } = string.Empty;
        public int DurationMinutes { get; set; }
    }
}
