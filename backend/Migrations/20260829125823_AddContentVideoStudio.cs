using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddContentVideoStudio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeminiEnterpriseProjectId",
                table: "ProjectSettings",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ContentVideos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Brief = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IdeaTitle = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Hook = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    AspectRatio = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Resolution = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RequestedSceneCount = table.Column<int>(type: "integer", nullable: false),
                    RequestedSceneDurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    KnowledgeDocumentCount = table.Column<int>(type: "integer", nullable: false),
                    KnowledgeWasTruncated = table.Column<bool>(type: "boolean", nullable: false),
                    KnowledgeSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PlannerModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VideoModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    FinalVideoObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    FinalVideoMimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentVideos", x => x.Id);
                    table.UniqueConstraint("AK_ContentVideos_Id_ProjectId", x => new { x.Id, x.ProjectId });
                });

            migrationBuilder.CreateTable(
                name: "ContentVideoScenes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContentVideoId = table.Column<Guid>(type: "uuid", nullable: false),
                    SceneIndex = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Narrative = table.Column<string>(type: "text", nullable: false),
                    VisualPrompt = table.Column<string>(type: "text", nullable: false),
                    AudioPrompt = table.Column<string>(type: "text", nullable: false),
                    TransitionPrompt = table.Column<string>(type: "text", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ProviderInteractionId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ProviderProjectId = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SubmissionClaimToken = table.Column<Guid>(type: "uuid", nullable: true),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GenerationStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TransientRetryCount = table.Column<int>(type: "integer", nullable: false),
                    ProviderSubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderPolledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VideoObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    VideoMimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentVideoScenes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentVideoScenes_ContentVideos_ContentVideoId_ProjectId",
                        columns: x => new { x.ContentVideoId, x.ProjectId },
                        principalTable: "ContentVideos",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentVideos_ProjectId_CreatedAt",
                table: "ContentVideos",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentVideos_ProjectId_Status_UpdatedAt",
                table: "ContentVideos",
                columns: new[] { "ProjectId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentVideos_Status_UpdatedAt",
                table: "ContentVideos",
                columns: new[] { "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentVideoScenes_ContentVideoId_ProjectId",
                table: "ContentVideoScenes",
                columns: new[] { "ContentVideoId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentVideoScenes_ContentVideoId_SceneIndex",
                table: "ContentVideoScenes",
                columns: new[] { "ContentVideoId", "SceneIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentVideoScenes_ProjectId_Status_UpdatedAt",
                table: "ContentVideoScenes",
                columns: new[] { "ProjectId", "Status", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentVideoScenes_Status_NextAttemptAtUtc_ProjectId",
                table: "ContentVideoScenes",
                columns: new[] { "Status", "NextAttemptAtUtc", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentVideoScenes");

            migrationBuilder.DropTable(
                name: "ContentVideos");

            migrationBuilder.DropColumn(
                name: "GeminiEnterpriseProjectId",
                table: "ProjectSettings");
        }
    }
}
