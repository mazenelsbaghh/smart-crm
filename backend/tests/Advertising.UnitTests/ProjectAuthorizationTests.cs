using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.AI.Services;
using Modules.Projects.API;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ProjectAuthorizationTests
{
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly ProjectAuthorizationService _authorization = new();
    private readonly ProjectSecretVault _secretVault = new(new EphemeralDataProtectionProvider());

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", true)]
    [InlineData("Agent", false)]
    [InlineData("Supervisor", false)]
    public void Only_owner_and_admin_can_manage_advertising(string role, bool expected)
    {
        Assert.Equal(expected, _authorization.CanManageAdvertising(User(role, _projectId), _projectId));
    }

    [Fact]
    public void Project_claim_cannot_authorize_another_project()
    {
        Assert.False(_authorization.CanRead(User("Owner", _projectId), Guid.NewGuid()));
        Assert.False(_authorization.CanManageAdvertising(User("Owner", _projectId), Guid.NewGuid()));
    }

    [Fact]
    public void Project_identity_comes_from_the_signed_claim()
    {
        var user = User("Admin", _projectId);

        Assert.Equal(_projectId, _authorization.GetProjectId(user));
        Assert.True(_authorization.CanManageProject(user, _projectId));
    }

    [Fact]
    public async Task Project_list_returns_only_the_workspace_from_the_signed_claim()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        db.Projects.AddRange(
            new Project { Id = _projectId, Name = "Claimed workspace" },
            new Project { Id = Guid.NewGuid(), Name = "Another workspace" });
        await db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await controller.List());
        var returnedProject = Assert.Single(Assert.IsAssignableFrom<IEnumerable<Project>>(result.Value));

        Assert.Equal(_projectId, returnedProject.Id);
    }

    [Fact]
    public async Task Project_creation_is_closed_until_a_trusted_onboarding_flow_exists()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;

        var result = Assert.IsType<ObjectResult>(controller.Create());

        Assert.Equal(403, result.StatusCode);
        var body = System.Text.Json.JsonSerializer.SerializeToElement(result.Value);
        Assert.Equal("PROJECT_CREATION_DISABLED", body.GetProperty("code").GetString());
        Assert.False(await db.Projects.IgnoreQueryFilters().AnyAsync());
    }

    [Fact]
    public async Task Missing_model_update_preserves_the_current_model()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest());

        Assert.IsType<OkObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("gemini-flash-latest", settings.GeminiModel);
    }

    [Fact]
    public async Task Planner_and_agent_platform_keys_are_protected_and_stored_separately()
    {
        const string plannerApiKey = "planner-key-sentinel";
        const string agentPlatformApiKey = "agent-platform-key-sentinel";
        const string enterpriseProjectId = "video-project";
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            GeminiApiKey = plannerApiKey,
            GeminiAgentPlatformApiKey = agentPlatformApiKey,
            GeminiEnterpriseProjectId = enterpriseProjectId
        });

        Assert.IsType<OkObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.NotEqual(plannerApiKey, settings.GeminiApiKey);
        Assert.NotEqual(agentPlatformApiKey, settings.GeminiAgentPlatformApiKey);
        Assert.Equal(plannerApiKey, _secretVault.Unprotect(_projectId, settings.GeminiApiKey));
        Assert.Equal(
            agentPlatformApiKey,
            _secretVault.Unprotect(_projectId, settings.GeminiAgentPlatformApiKey));
        Assert.Equal(enterpriseProjectId, settings.GeminiEnterpriseProjectId);
    }

    [Fact]
    public async Task Unsupported_model_update_is_rejected_without_changing_settings()
    {
        var (controller, db) = CreateController("Admin", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            GeminiModel = "gemini-legacy"
        });

        Assert.IsType<BadRequestObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("gemini-flash-latest", settings.GeminiModel);
    }

    [Fact]
    public async Task Temporary_model_override_keeps_the_base_model_and_sets_an_expiry()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-lite-latest");
        var startedAt = DateTime.UtcNow;

        var response = await controller.ActivateGeminiModelOverride(
            _projectId,
            new TemporaryGeminiModelRequest
            {
                Model = "gemini-flash-latest",
                DurationMinutes = 120
            });

        Assert.IsType<OkObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("gemini-flash-lite-latest", settings.GeminiModel);
        Assert.Equal("gemini-flash-latest", settings.ResolveGeminiModel(startedAt));
        Assert.InRange(
            settings.TemporaryGeminiModelExpiresAtUtc!.Value,
            startedAt.AddMinutes(119),
            startedAt.AddMinutes(121));
    }

    [Fact]
    public void Expired_temporary_model_automatically_resolves_to_the_base_model()
    {
        var now = DateTime.UtcNow;
        var settings = new ProjectSettings
        {
            GeminiModel = "gemini-flash-lite-latest",
            TemporaryGeminiModel = "gemini-flash-latest",
            TemporaryGeminiModelExpiresAtUtc = now.AddSeconds(-1)
        };

        Assert.Equal("gemini-flash-lite-latest", settings.ResolveGeminiModel(now));
        Assert.False(settings.HasActiveTemporaryGeminiModel(now));
    }

    [Fact]
    public async Task Temporary_model_override_rejects_an_unsupported_duration()
    {
        var (controller, db) = CreateController("Admin", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-lite-latest");

        var response = await controller.ActivateGeminiModelOverride(
            _projectId,
            new TemporaryGeminiModelRequest
            {
                Model = "gemini-flash-latest",
                DurationMinutes = 5
            });

        Assert.IsType<BadRequestObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Null(settings.TemporaryGeminiModel);
        Assert.Null(settings.TemporaryGeminiModelExpiresAtUtc);
    }

    [Fact]
    public async Task Cancelling_temporary_model_returns_to_the_base_model_immediately()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-lite-latest");
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        settings.TemporaryGeminiModel = "gemini-flash-latest";
        settings.TemporaryGeminiModelExpiresAtUtc = DateTime.UtcNow.AddHours(2);
        await db.SaveChangesAsync();

        var response = await controller.CancelGeminiModelOverride(_projectId);

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal("gemini-flash-lite-latest", settings.ResolveGeminiModel(DateTime.UtcNow));
        Assert.Null(settings.TemporaryGeminiModel);
        Assert.Null(settings.TemporaryGeminiModelExpiresAtUtc);
    }

    [Fact]
    public async Task OpenAI_customer_replies_require_a_separate_project_key()
    {
        var (controller, db) = CreateController("Admin", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            CustomerReplyProvider = "OpenAI",
            CustomerReplyModel = "gpt-5.6"
        });

        Assert.IsType<BadRequestObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Gemini", settings.CustomerReplyProvider);
        Assert.Equal("gemini-flash-latest", settings.GeminiModel);
    }

    [Fact]
    public async Task OpenAI_customer_reply_settings_are_stored_separately_from_Gemini()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            CustomerReplyProvider = "OpenAI",
            CustomerReplyOpenAiApiKey = "sk-project-chat",
            CustomerReplyModel = "gpt-5.6-terra"
        });

        Assert.IsType<OkObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("OpenAI", settings.CustomerReplyProvider);
        Assert.Equal("gpt-5.6-terra", settings.CustomerReplyModel);
        Assert.NotEqual("sk-project-chat", settings.CustomerReplyOpenAiApiKey);
        Assert.Equal("gemini-flash-latest", settings.GeminiModel);
        Assert.Equal(string.Empty, settings.GeminiApiKey);
    }

    [Fact]
    public async Task Unsupported_OpenAI_customer_reply_model_is_rejected()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            CustomerReplyProvider = "OpenAI",
            CustomerReplyOpenAiApiKey = "openai-test-unsupported-model-key",
            CustomerReplyModel = "gpt-5.6-unknown"
        });

        Assert.IsType<BadRequestObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Gemini", settings.CustomerReplyProvider);
        Assert.Equal("gpt-5.6", settings.CustomerReplyModel);
        Assert.Equal(string.Empty, settings.CustomerReplyOpenAiApiKey);
    }

    [Fact]
    public async Task XAI_customer_replies_require_a_separate_project_key()
    {
        var (controller, db) = CreateController("Admin", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            CustomerReplyProvider = "xAI",
            CustomerReplyModel = "grok-4.6"
        });

        Assert.IsType<BadRequestObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Gemini", settings.CustomerReplyProvider);
        Assert.Equal(string.Empty, settings.CustomerReplyXaiApiKey);
    }

    [Fact]
    public async Task XAI_customer_reply_key_is_protected_and_stored_separately()
    {
        const string rawXaiKey = "xai-test-project-key";
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");
        var seededSettings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        seededSettings.GeminiApiKey = _secretVault.Protect(_projectId, "gemini-test-project-key");
        seededSettings.CustomerReplyOpenAiApiKey = _secretVault.Protect(_projectId, "openai-test-project-key");
        var protectedGeminiKey = seededSettings.GeminiApiKey;
        var protectedOpenAiKey = seededSettings.CustomerReplyOpenAiApiKey;
        await db.SaveChangesAsync();

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            CustomerReplyProvider = "xAI",
            CustomerReplyXaiApiKey = rawXaiKey,
            CustomerReplyModel = "grok-4.6"
        });

        Assert.IsType<OkObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("xAI", settings.CustomerReplyProvider);
        Assert.Equal("grok-4.6", settings.CustomerReplyModel);
        Assert.NotEqual(rawXaiKey, settings.CustomerReplyXaiApiKey);
        Assert.Equal(rawXaiKey, _secretVault.Unprotect(_projectId, settings.CustomerReplyXaiApiKey));
        Assert.Equal(protectedGeminiKey, settings.GeminiApiKey);
        Assert.Equal(protectedOpenAiKey, settings.CustomerReplyOpenAiApiKey);
    }

    [Fact]
    public async Task XAI_customer_reply_key_can_be_cleared_after_switching_provider()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");
        var seededSettings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        seededSettings.CustomerReplyProvider = "xAI";
        seededSettings.CustomerReplyModel = "grok-4.6";
        seededSettings.CustomerReplyXaiApiKey = _secretVault.Protect(_projectId, "xai-test-key-to-clear");
        await db.SaveChangesAsync();

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            CustomerReplyProvider = "Gemini",
            ClearCustomerReplyXaiApiKey = true
        });

        Assert.IsType<OkObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Gemini", settings.CustomerReplyProvider);
        Assert.Equal(string.Empty, settings.CustomerReplyXaiApiKey);
    }

    [Fact]
    public async Task Supported_Grok_customer_reply_model_can_be_selected()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            CustomerReplyProvider = "xAI",
            CustomerReplyXaiApiKey = "xai-test-secondary-model-key",
            CustomerReplyModel = "grok-4.3"
        });

        Assert.IsType<OkObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("grok-4.3", settings.CustomerReplyModel);
    }

    [Fact]
    public async Task Unsupported_Grok_customer_reply_model_is_rejected_without_changing_settings()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            CustomerReplyProvider = "xAI",
            CustomerReplyXaiApiKey = "xai-test-unsupported-model-key",
            CustomerReplyModel = "grok-unsupported"
        });

        Assert.IsType<BadRequestObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Gemini", settings.CustomerReplyProvider);
        Assert.Equal("gpt-5.6", settings.CustomerReplyModel);
        Assert.Equal(string.Empty, settings.CustomerReplyXaiApiKey);
    }

    [Fact]
    public async Task Project_get_reports_xai_key_configuration_without_returning_the_secret()
    {
        const string rawXaiKey = "xai-test-get-settings-key";
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        await SeedProjectAsync(db, "gemini-flash-latest");
        var seededSettings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        seededSettings.CustomerReplyProvider = "xAI";
        seededSettings.CustomerReplyModel = "grok-4.6";
        seededSettings.CustomerReplyXaiApiKey = _secretVault.Protect(_projectId, rawXaiKey);
        await db.SaveChangesAsync();

        var response = Assert.IsType<OkObjectResult>(await controller.Get(_projectId));
        var payload = JsonSerializer.SerializeToElement(
            response.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var settings = payload.GetProperty("settings");

        Assert.True(settings.GetProperty("customerReplyXaiApiKeyConfigured").GetBoolean());
        Assert.False(settings.TryGetProperty("customerReplyXaiApiKey", out _));
        Assert.DoesNotContain(rawXaiKey, payload.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task First_settings_update_persists_channel_automation_options()
    {
        var (controller, db) = CreateController("Owner", _projectId);
        await using var ownedDb = db;
        db.Projects.Add(new Project { Id = _projectId, Name = "Authorized project" });
        await db.SaveChangesAsync();

        var response = await controller.UpdateSettings(_projectId, new UpdateSettingsRequest
        {
            Timezone = "Africa/Cairo",
            MessengerAiAutoReplyEnabled = true,
            MessengerReplyDelay = 7,
            CommentsAiAutoReplyEnabled = true,
            CommentsReplyDelay = 11
        });

        Assert.IsType<OkObjectResult>(response);
        var settings = await db.ProjectSettings.IgnoreQueryFilters().SingleAsync();
        Assert.True(settings.MessengerAiAutoReplyEnabled);
        Assert.Equal(7, settings.MessengerReplyDelay);
        Assert.True(settings.CommentsAiAutoReplyEnabled);
        Assert.Equal(11, settings.CommentsReplyDelay);
    }

    private (ProjectController Controller, AppDbContext Db) CreateController(string role, Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
        var controller = new ProjectController(
            db,
            new AIBehaviorSettingsService(),
            _authorization,
            _secretVault)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = User(role, projectId) }
            }
        };
        return (controller, db);
    }

    private async Task SeedProjectAsync(AppDbContext db, string model)
    {
        db.Projects.Add(new Project { Id = _projectId, Name = "Authorized project" });
        db.ProjectSettings.Add(new ProjectSettings
        {
            ProjectId = _projectId,
            GeminiModel = model,
            Timezone = "Africa/Cairo"
        });
        await db.SaveChangesAsync();
    }

    private static ClaimsPrincipal User(string role, Guid projectId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
        new Claim(ClaimTypes.Role, role),
        new Claim("ProjectId", projectId.ToString())
    ], "test"));
}
