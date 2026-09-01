using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddContentWeeklyPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentWeekPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartDateLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    DailyPublishTimeLocal = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Timezone = table.Column<string>(type: "text", nullable: false),
                    BrandLogoObjectKey = table.Column<string>(type: "text", nullable: false),
                    BrandStylePrompt = table.Column<string>(type: "text", nullable: false),
                    KnowledgeDocumentCount = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentWeekPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentWeekPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayIndex = table.Column<int>(type: "integer", nullable: false),
                    ScheduledForUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Topic = table.Column<string>(type: "text", nullable: false),
                    VisualHeadline = table.Column<string>(type: "text", nullable: false),
                    Caption = table.Column<string>(type: "text", nullable: false),
                    ImagePrompt = table.Column<string>(type: "text", nullable: false),
                    ContentPostId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentWeekPlanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentWeekPlanItems_ContentWeekPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "ContentWeekPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentWeekPlanItems_PlanId_DayIndex",
                table: "ContentWeekPlanItems",
                columns: new[] { "PlanId", "DayIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentWeekPlanItems_ProjectId_ScheduledForUtc",
                table: "ContentWeekPlanItems",
                columns: new[] { "ProjectId", "ScheduledForUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentWeekPlans_ProjectId_Status_CreatedAt",
                table: "ContentWeekPlans",
                columns: new[] { "ProjectId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentWeekPlanItems");

            migrationBuilder.DropTable(
                name: "ContentWeekPlans");
        }
    }
}
