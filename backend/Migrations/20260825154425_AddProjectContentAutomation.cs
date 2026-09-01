using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectContentAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentAutomationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookPageId = table.Column<string>(type: "text", nullable: true),
                    FacebookPageName = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    HasApprovedStyle = table.Column<bool>(type: "boolean", nullable: false),
                    DailyPublishTimeLocal = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Timezone = table.Column<string>(type: "text", nullable: false),
                    NextPublishAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    LogoObjectKey = table.Column<string>(type: "text", nullable: true),
                    LogoMimeType = table.Column<string>(type: "text", nullable: true),
                    LogoFileName = table.Column<string>(type: "text", nullable: true),
                    BrandColorsJson = table.Column<string>(type: "text", nullable: false),
                    StylePrompt = table.Column<string>(type: "text", nullable: false),
                    ApprovedSamplePostId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentAutomationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentPosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    IsStyleSample = table.Column<bool>(type: "boolean", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: false),
                    VisualHeadline = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    ImagePrompt = table.Column<string>(type: "text", nullable: false),
                    BrandLogoObjectKey = table.Column<string>(type: "text", nullable: false),
                    BrandStylePrompt = table.Column<string>(type: "text", nullable: false),
                    ImageObjectKey = table.Column<string>(type: "text", nullable: true),
                    ImageMimeType = table.Column<string>(type: "text", nullable: false),
                    ImageModel = table.Column<string>(type: "text", nullable: false),
                    ImageSize = table.Column<string>(type: "text", nullable: false),
                    KnowledgeDocumentCount = table.Column<int>(type: "integer", nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FacebookPostId = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPosts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentAutomationSettings_ProjectId",
                table: "ContentAutomationSettings",
                column: "ProjectId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentPosts_ProjectId_CreatedAt",
                table: "ContentPosts",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPosts_ProjectId_ScheduledForUtc",
                table: "ContentPosts",
                columns: new[] { "ProjectId", "ScheduledForUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentAutomationSettings");

            migrationBuilder.DropTable(
                name: "ContentPosts");
        }
    }
}
