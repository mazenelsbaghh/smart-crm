using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.AI.Services;
using Modules.Brain.Domain;
using Modules.Content.Domain;
using Modules.Content.Services;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ContentVideoTests
{
    private const string EnterpriseProjectId = "video-project";
    private const string PlannerApiKey = "planner-key-sentinel";
    private const string AgentPlatformApiKey = "agent-platform-key-sentinel";

    [Fact]
    public async Task Agent_platform_key_submission_uses_the_official_background_inline_video_contract()
    {
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;
        string? requestApiKey = null;
        string? requestJson = null;
        var client = CreateVideoClient(new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestMethod = request.Method;
            requestUri = request.RequestUri;
            requestApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("""{"id":"interaction-1","status":"in_progress"}""");
        }));

        var interaction = await client.SubmitAsync(
            VideoRequest("Create a grounded product scene"),
            CancellationToken.None);

        Assert.Equal(GeminiOmniInteractionStatus.InProgress, interaction.Status);
        Assert.Equal(HttpMethod.Post, requestMethod);
        Assert.Equal(
            new Uri("https://aiplatform.googleapis.com/v1beta1/projects/video-project/locations/global/interactions"),
            requestUri);
        Assert.Equal(AgentPlatformApiKey, requestApiKey);

        using var document = JsonDocument.Parse(requestJson!);
        var body = document.RootElement;
        Assert.Equal(ContentVideoCapabilities.Model, body.GetProperty("model").GetString());
        Assert.True(body.GetProperty("background").GetBoolean());

        var input = body.GetProperty("input");
        Assert.Equal(1, input.GetArrayLength());
        Assert.Equal("text", input[0].GetProperty("type").GetString());
        Assert.Equal("Create a grounded product scene", input[0].GetProperty("text").GetString());
        Assert.False(input[0].TryGetProperty("background", out _));

        var format = Assert.Single(body.GetProperty("response_format").EnumerateArray());
        Assert.Equal("video", format.GetProperty("type").GetString());
        Assert.Equal("9:16", format.GetProperty("aspect_ratio").GetString());
        Assert.Equal("1080p", format.GetProperty("resolution").GetString());
        Assert.Equal("6s", format.GetProperty("duration").GetString());
        Assert.False(format.TryGetProperty("delivery", out _));
        Assert.False(format.TryGetProperty("gcs_uri", out _));
        Assert.Equal(
            "text_to_video",
            body.GetProperty("generation_config")
                .GetProperty("video_config")
                .GetProperty("task")
                .GetString());
    }

    [Fact]
    public async Task Submission_without_an_agent_platform_key_fails_before_calling_the_provider()
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(
                new InvalidOperationException(
                    "The provider must not be called without an Agent Platform key."))));
        var request = VideoRequest("Generate a product scene") with { ApiKey = null };

        var exception = await Assert.ThrowsAsync<ContentVideoException>(() =>
            client.SubmitAsync(request, CancellationToken.None));

        Assert.Equal("OMNI_AUTH_MISSING", exception.Code);
    }

    [Fact]
    public async Task Starting_frame_submission_uses_png_image_to_video_contract()
    {
        var firstFrame = new byte[] { 137, 80, 78, 71, 1, 2, 3 };
        string? requestJson = null;
        var client = CreateVideoClient(new StubHttpMessageHandler(async (request, cancellationToken) =>
        {
            requestJson = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse("""{"id":"interaction-image","status":"in_progress"}""");
        }));

        await client.SubmitAsync(
            VideoRequest("Continue from this frame", firstFrame),
            CancellationToken.None);

        using var document = JsonDocument.Parse(requestJson!);
        var body = document.RootElement;
        var input = body.GetProperty("input");
        Assert.Equal(2, input.GetArrayLength());
        Assert.Equal("image", input[1].GetProperty("type").GetString());
        Assert.Equal("image/png", input[1].GetProperty("mime_type").GetString());
        Assert.Equal(Convert.ToBase64String(firstFrame), input[1].GetProperty("data").GetString());
        Assert.Equal(
            "image_to_video",
            body.GetProperty("generation_config")
                .GetProperty("video_config")
                .GetProperty("task")
                .GetString());
    }

    [Fact]
    public async Task Completed_inline_poll_decodes_video_and_uses_get_interaction_path()
    {
        HttpMethod? requestMethod = null;
        Uri? requestUri = null;
        string? requestApiKey = null;
        var client = CreateVideoClient(new StubHttpMessageHandler((request, _) =>
        {
            requestMethod = request.Method;
            requestUri = request.RequestUri;
            requestApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            return Task.FromResult(JsonResponse("""
                {
                  "id": "interaction-123",
                  "status": "completed",
                  "steps": [
                    { "type": "thought", "summary": [] },
                    {
                      "type": "model_output",
                      "content": [
                        { "type": "video", "data": "AQIDBA==", "mime_type": "video/webm" }
                      ]
                    }
                  ]
                }
                """));
        }));

        var interaction = await client.GetAsync(
            EnterpriseProjectId,
            "interaction-123",
            AgentPlatformApiKey,
            CancellationToken.None);

        Assert.Equal(HttpMethod.Get, requestMethod);
        Assert.Equal(
            new Uri("https://aiplatform.googleapis.com/v1beta1/projects/video-project/locations/global/interactions/interaction-123"),
            requestUri);
        Assert.Equal(AgentPlatformApiKey, requestApiKey);
        Assert.Equal("interaction-123", interaction.InteractionId);
        Assert.Equal(GeminiOmniInteractionStatus.Completed, interaction.Status);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, interaction.VideoBytes);
        Assert.Equal("video/webm", interaction.VideoMimeType);
    }

    [Theory]
    [InlineData("in_progress", GeminiOmniInteractionStatus.InProgress)]
    [InlineData("failed", GeminiOmniInteractionStatus.Failed)]
    [InlineData("cancelled", GeminiOmniInteractionStatus.Cancelled)]
    [InlineData("incomplete", GeminiOmniInteractionStatus.Incomplete)]
    [InlineData("requires_action", GeminiOmniInteractionStatus.RequiresAction)]
    public async Task Non_completed_official_statuses_are_mapped_without_video_output(
        string providerStatus,
        GeminiOmniInteractionStatus expectedStatus)
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(JsonSerializer.Serialize(new
            {
                id = "interaction-status",
                status = providerStatus
            })))));

        var interaction = await client.GetAsync(
            EnterpriseProjectId,
            "interaction-status",
            AgentPlatformApiKey,
            CancellationToken.None);

        Assert.Equal(expectedStatus, interaction.Status);
        Assert.Null(interaction.VideoBytes);
        Assert.Null(interaction.VideoMimeType);
    }

    [Fact]
    public async Task Unknown_interaction_status_is_rejected_safely()
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("""{"id":"interaction-unknown","status":"paused"}"""))));

        var exception = await Assert.ThrowsAsync<ContentVideoException>(() => client.GetAsync(
            EnterpriseProjectId,
            "interaction-unknown",
            AgentPlatformApiKey,
            CancellationToken.None));

        Assert.Equal("OMNI_STATUS_UNKNOWN", exception.Code);
    }

    [Fact]
    public async Task Poll_rate_limit_preserves_retry_after_for_safe_rescheduling()
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(23));
            return Task.FromResult(response);
        }));

        var exception = await Assert.ThrowsAsync<GeminiOmniRetryableException>(() => client.GetAsync(
            EnterpriseProjectId,
            "interaction-rate-limited",
            AgentPlatformApiKey,
            CancellationToken.None));

        Assert.Equal("OMNI_HTTP_429", exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(23), exception.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, "OMNI_HTTP_408")]
    [InlineData(HttpStatusCode.InternalServerError, "OMNI_HTTP_500")]
    public async Task Poll_transient_http_failures_are_retryable(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode))));

        var exception = await Assert.ThrowsAsync<GeminiOmniRetryableException>(() => client.GetAsync(
            EnterpriseProjectId,
            "interaction-transient-failure",
            AgentPlatformApiKey,
            CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task Submission_rate_limit_is_retryable_without_marking_the_outcome_uncertain()
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(19));
            return Task.FromResult(response);
        }));

        var exception = await Assert.ThrowsAsync<GeminiOmniRetryableException>(() =>
            client.SubmitAsync(VideoRequest("Generate a product scene"), CancellationToken.None));

        Assert.Equal("OMNI_HTTP_429", exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(19), exception.RetryAfter);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, "OMNI_HTTP_408_SUBMISSION_UNCERTAIN")]
    [InlineData(HttpStatusCode.InternalServerError, "OMNI_HTTP_500_SUBMISSION_UNCERTAIN")]
    public async Task Submission_ambiguous_http_failures_are_reported_as_uncertain(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode))));

        var exception = await Assert.ThrowsAsync<GeminiOmniSubmissionUncertainException>(() =>
            client.SubmitAsync(VideoRequest("Generate a product scene"), CancellationToken.None));

        Assert.Equal(expectedCode, exception.Code);
    }

    [Fact]
    public async Task Submission_transport_failure_is_reported_as_an_uncertain_outcome()
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("connection reset"))));

        var exception = await Assert.ThrowsAsync<GeminiOmniSubmissionUncertainException>(() =>
            client.SubmitAsync(VideoRequest("Generate a product scene"), CancellationToken.None));

        Assert.Equal("OMNI_UNAVAILABLE_SUBMISSION_UNCERTAIN", exception.Code);
    }

    [Fact]
    public async Task Malformed_submission_preserves_a_valid_interaction_id_for_safe_recovery()
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(
                """{"id":"interaction-recoverable","status":"completed"}"""))));

        var exception = await Assert.ThrowsAsync<GeminiOmniSubmissionUncertainException>(() =>
            client.SubmitAsync(VideoRequest("Generate a product scene"), CancellationToken.None));

        Assert.Equal("OMNI_SUBMISSION_RESPONSE_UNCERTAIN", exception.Code);
        Assert.Equal("interaction-recoverable", exception.InteractionId);
    }

    [Fact]
    public async Task Oversized_poll_response_is_rejected_before_the_body_is_read()
    {
        var client = CreateVideoClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new OversizedUnreadableContent()
            })));

        var exception = await Assert.ThrowsAsync<ContentVideoException>(() => client.GetAsync(
            EnterpriseProjectId,
            "interaction-too-large",
            AgentPlatformApiKey,
            CancellationToken.None));

        Assert.Equal("OMNI_RESPONSE_TOO_LARGE", exception.Code);
    }

    [Fact]
    public async Task Planning_uses_only_grounded_project_context_and_creates_three_reviewable_scenes()
    {
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var video = PlanningVideo(projectId);
        dbContext.AddRange(
            new ProjectSettings
            {
                ProjectId = projectId,
                GeminiApiKey = PlannerApiKey,
                GeminiAgentPlatformApiKey = AgentPlatformApiKey,
                GeminiModel = "gemini-3.5-flash"
            },
            video,
            Knowledge(projectId, "approved-knowledge-marker", "Approved"),
            Knowledge(projectId, "draft-knowledge-marker", "Draft"),
            Knowledge(otherProjectId, "other-project-knowledge-marker", "Approved"),
            new ContentVideo
            {
                ProjectId = projectId,
                Status = ContentVideoStatus.Ready,
                IdeaTitle = "previous-idea-marker",
                Hook = "previous hook",
                Summary = "previous summary"
            },
            new ContentVideo
            {
                ProjectId = otherProjectId,
                Status = ContentVideoStatus.Ready,
                IdeaTitle = "other-project-idea-marker",
                Hook = "other hook",
                Summary = "other summary"
            });
        await dbContext.SaveChangesAsync();
        var gemini = new RecordingGeminiClient(PlanJson(sceneCount: 3));
        var service = new ContentVideoPlanningService(
            dbContext,
            gemini,
            new PassThroughSecretVault());

        await service.PlanAsync(projectId, video.Id, CancellationToken.None);

        Assert.Equal(PlannerApiKey, gemini.LastApiKeyOverride);
        dbContext.ChangeTracker.Clear();
        var savedVideo = await dbContext.ContentVideos.SingleAsync(candidate => candidate.Id == video.Id);
        var scenes = await dbContext.ContentVideoScenes
            .Where(scene => scene.ContentVideoId == video.Id)
            .OrderBy(scene => scene.SceneIndex)
            .ToListAsync();
        Assert.Equal(ContentVideoStatus.AwaitingApproval, savedVideo.Status);
        Assert.Equal("fresh grounded idea", savedVideo.IdeaTitle);
        Assert.Equal("fresh hook", savedVideo.Hook);
        Assert.Equal("fresh summary", savedVideo.Summary);
        Assert.Equal("fresh caption", savedVideo.Caption);
        Assert.Equal(1, savedVideo.KnowledgeDocumentCount);
        Assert.False(string.IsNullOrWhiteSpace(savedVideo.KnowledgeSnapshotHash));
        Assert.Equal(3, scenes.Count);
        Assert.Equal(new[] { 0, 1, 2 }, scenes.Select(scene => scene.SceneIndex));
        Assert.All(scenes, scene =>
        {
            Assert.Equal(projectId, scene.ProjectId);
            Assert.Equal(ContentVideoSceneStatus.Planned, scene.Status);
            Assert.Equal(6, scene.DurationSeconds);
        });
        Assert.Equal("scene 1", scenes[0].Title);
        Assert.Equal("narrative 1", scenes[0].Narrative);
        Assert.Equal("Visual direction 1", scenes[0].VisualPrompt);
        Assert.Equal("audio direction 1", scenes[0].AudioPrompt);
        Assert.Equal("transition 1", scenes[0].TransitionPrompt);

        Assert.NotNull(gemini.LastPrompt);
        Assert.Contains("approved-knowledge-marker", gemini.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("draft-knowledge-marker", gemini.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("other-project-knowledge-marker", gemini.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("previous-idea-marker", gemini.LastPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("other-project-idea-marker", gemini.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("\"idea\"", gemini.LastPrompt, StringComparison.Ordinal);
        Assert.Contains("\"scenes\"", gemini.LastPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, true, true, true)]
    public async Task Video_readiness_requires_both_keys_enterprise_project_and_approved_knowledge(
        bool plannerKeyConfigured,
        bool agentPlatformKeyConfigured,
        bool enterpriseProjectConfigured,
        bool approvedKnowledgeConfigured,
        bool expectedConfigured)
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        dbContext.ProjectSettings.Add(new ProjectSettings
        {
            ProjectId = projectId,
            GeminiApiKey = plannerKeyConfigured ? PlannerApiKey : string.Empty,
            GeminiAgentPlatformApiKey = agentPlatformKeyConfigured
                ? AgentPlatformApiKey
                : string.Empty,
            GeminiEnterpriseProjectId = enterpriseProjectConfigured ? EnterpriseProjectId : null
        });
        if (approvedKnowledgeConfigured)
            dbContext.KnowledgeDocuments.Add(
                Knowledge(projectId, "approved-knowledge-marker", "Approved"));
        await dbContext.SaveChangesAsync();
        var service = new ContentVideoReadinessService(dbContext);

        var readiness = await service.GetAsync(projectId, CancellationToken.None);

        Assert.Equal(expectedConfigured, readiness.Configured);
        Assert.Equal(plannerKeyConfigured, readiness.GeminiApiKeyConfigured);
        Assert.Equal(
            agentPlatformKeyConfigured,
            readiness.GeminiAgentPlatformApiKeyConfigured);
        Assert.Equal(approvedKnowledgeConfigured ? 1 : 0, readiness.KnowledgeDocumentCount);
        if (expectedConfigured)
            Assert.Null(readiness.Reason);
        else
            Assert.NotNull(readiness.Reason);
    }

    [Fact]
    public async Task Plan_with_a_different_scene_count_is_rejected_without_saving_scenes()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var video = PlanningVideo(projectId);
        dbContext.AddRange(
            new ProjectSettings
            {
                ProjectId = projectId,
                GeminiApiKey = PlannerApiKey,
                GeminiModel = "gemini-3.5-flash"
            },
            video,
            Knowledge(projectId, "approved-knowledge-marker", "Approved"));
        await dbContext.SaveChangesAsync();
        var service = new ContentVideoPlanningService(
            dbContext,
            new RecordingGeminiClient(PlanJson(sceneCount: 4)),
            new PassThroughSecretVault());

        var exception = await Assert.ThrowsAsync<ContentVideoException>(() =>
            service.PlanAsync(projectId, video.Id, CancellationToken.None));

        Assert.Equal("INVALID_VIDEO_PLAN", exception.Code);
        Assert.Equal(ContentVideoStatus.Planning, video.Status);
        Assert.False(await dbContext.ContentVideoScenes.AnyAsync(
            scene => scene.ContentVideoId == video.Id));
    }

    [Fact]
    public async Task Normalized_duplicate_idea_is_rejected_without_saving_scenes()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var video = PlanningVideo(projectId);
        dbContext.AddRange(
            new ProjectSettings
            {
                ProjectId = projectId,
                GeminiApiKey = PlannerApiKey,
                GeminiModel = "gemini-3.5-flash"
            },
            video,
            Knowledge(projectId, "approved-knowledge-marker", "Approved"),
            new ContentVideo
            {
                ProjectId = projectId,
                Status = ContentVideoStatus.Ready,
                IdeaTitle = " FRESH\tGROUNDED\nIDEA ",
                Hook = "previous hook",
                Summary = "previous summary"
            });
        await dbContext.SaveChangesAsync();
        var service = new ContentVideoPlanningService(
            dbContext,
            new RecordingGeminiClient(PlanJson(sceneCount: 3)),
            new PassThroughSecretVault());

        var exception = await Assert.ThrowsAsync<ContentVideoException>(() =>
            service.PlanAsync(projectId, video.Id, CancellationToken.None));

        Assert.Equal("DUPLICATE_VIDEO_IDEA", exception.Code);
        Assert.Equal(ContentVideoStatus.Planning, video.Status);
        Assert.False(await dbContext.ContentVideoScenes.AnyAsync(
            scene => scene.ContentVideoId == video.Id));
    }

    [Fact]
    public async Task Grounding_truncation_does_not_pass_a_split_unicode_scalar_to_the_planner()
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var video = PlanningVideo(projectId);
        var knowledge = Knowledge(projectId, "unicode-boundary", "Approved");
        knowledge.Content = new string('a', 23_999) + "😀tail";
        dbContext.AddRange(
            new ProjectSettings
            {
                ProjectId = projectId,
                GeminiApiKey = PlannerApiKey,
                GeminiModel = "gemini-3.5-flash"
            },
            video,
            knowledge);
        await dbContext.SaveChangesAsync();
        var gemini = new RecordingGeminiClient(PlanJson(sceneCount: 3));
        var service = new ContentVideoPlanningService(
            dbContext,
            gemini,
            new PassThroughSecretVault());

        await service.PlanAsync(projectId, video.Id, CancellationToken.None);

        dbContext.ChangeTracker.Clear();
        var savedVideo = await dbContext.ContentVideos.SingleAsync(candidate => candidate.Id == video.Id);
        Assert.True(savedVideo.KnowledgeWasTruncated);
        Assert.NotNull(gemini.LastPrompt);
        Assert.DoesNotContain("\\uFFFD", gemini.LastPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("�", gemini.LastPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(301, 10, 10)]
    [InlineData(10, 1_001, 10)]
    [InlineData(10, 10, 301)]
    public async Task Plan_text_exceeding_persisted_limits_is_rejected(
        int titleLength,
        int hookLength,
        int sceneTitleLength)
    {
        var projectId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(projectId);
        var video = PlanningVideo(projectId);
        dbContext.AddRange(
            new ProjectSettings
            {
                ProjectId = projectId,
                GeminiApiKey = PlannerApiKey,
                GeminiModel = "gemini-3.5-flash"
            },
            video,
            Knowledge(projectId, "approved-knowledge-marker", "Approved"));
        await dbContext.SaveChangesAsync();
        var service = new ContentVideoPlanningService(
            dbContext,
            new RecordingGeminiClient(PlanJson(
                sceneCount: 3,
                title: new string('t', titleLength),
                hook: new string('h', hookLength),
                sceneTitle: new string('s', sceneTitleLength))),
            new PassThroughSecretVault());

        var exception = await Assert.ThrowsAsync<ContentVideoException>(() =>
            service.PlanAsync(projectId, video.Id, CancellationToken.None));

        Assert.Equal("INVALID_VIDEO_PLAN", exception.Code);
        Assert.False(await dbContext.ContentVideoScenes.AnyAsync(
            scene => scene.ContentVideoId == video.Id));
    }

    private static GeminiOmniVideoRequest VideoRequest(string prompt, byte[]? firstFrame = null) => new(
        EnterpriseProjectId,
        prompt,
        "9:16",
        "1080p",
        6,
        AgentPlatformApiKey,
        firstFrame);

    private static GeminiOmniVideoClient CreateVideoClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://aiplatform.googleapis.com/")
        });

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static AppDbContext CreateDbContext(Guid projectId)
    {
        var tenantContext = new TenantContext();
        tenantContext.SetProjectId(projectId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantContext,
            new ServiceCollection().BuildServiceProvider());
    }

    private static ContentVideo PlanningVideo(Guid projectId) => new()
    {
        ProjectId = projectId,
        Status = ContentVideoStatus.Planning,
        Brief = "Launch the grounded service",
        AspectRatio = "9:16",
        Resolution = "1080p",
        RequestedSceneCount = 3,
        RequestedSceneDurationSeconds = 6
    };

    private static KnowledgeDocument Knowledge(Guid projectId, string marker, string status) => new()
    {
        ProjectId = projectId,
        Title = marker,
        Content = $"grounding-content-{marker}",
        Status = status
    };

    private static string PlanJson(
        int sceneCount,
        string? title = null,
        string? hook = null,
        string? sceneTitle = null) => JsonSerializer.Serialize(new
    {
        idea = new
        {
            title = title ?? "fresh grounded idea",
            hook = hook ?? "fresh hook",
            summary = "fresh summary",
            caption = "fresh caption",
            scenes = Enumerable.Range(1, sceneCount).Select(index => new
            {
                title = sceneTitle ?? $"scene {index}",
                narrative = $"narrative {index}",
                visualPrompt = $"Visual direction {index}",
                audioPrompt = $"audio direction {index}",
                transitionPrompt = $"transition {index}",
                durationSeconds = 6
            })
        }
    });

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => handler(request, cancellationToken);
    }

    private sealed class OversizedUnreadableContent : HttpContent
    {
        public OversizedUnreadableContent() => Headers.ContentLength = long.MaxValue;

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new InvalidOperationException("Oversized content must be rejected before its body is read.");

        protected override bool TryComputeLength(out long length)
        {
            length = long.MaxValue;
            return true;
        }
    }

    private sealed class PassThroughSecretVault : IProjectSecretVault
    {
        public bool IsProtected(string? storedValue) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? storedValue) => storedValue;
    }

    private sealed class RecordingGeminiClient(string response) : IGeminiClient
    {
        public string? LastPrompt { get; private set; }
        public string? LastApiKeyOverride { get; private set; }

        public Task<string> GenerateReplyAsync(
            string messageContent,
            string apiKeyOverride = null!,
            string modelOverride = null!,
            string cachedContentId = null!)
        {
            LastPrompt = messageContent;
            LastApiKeyOverride = apiKeyOverride;
            return Task.FromResult(response);
        }

        public Task<string> GenerateReplyAsync(
            string messageContent,
            byte[] fileBytes,
            string mimeType,
            string apiKeyOverride = null!,
            string modelOverride = null!,
            string cachedContentId = null!) => throw new NotSupportedException();

        public Task<float[]> GenerateEmbeddingAsync(string text, string apiKeyOverride = null!) =>
            throw new NotSupportedException();

        public Task<int> CountTokensAsync(
            string messageContent,
            string apiKeyOverride = null!,
            string modelOverride = null!) => throw new NotSupportedException();

        public Task<string> CreateContextCacheAsync(
            string staticContent,
            string model,
            int ttlSeconds,
            string apiKeyOverride = null!) => throw new NotSupportedException();
    }
}
