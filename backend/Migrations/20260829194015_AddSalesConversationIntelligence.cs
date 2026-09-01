using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddSalesConversationIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversationSalesAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnalyzedThroughMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AnalyzedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AiStage = table.Column<int>(type: "integer", nullable: false),
                    VerifiedStage = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    AiPrimaryReason = table.Column<int>(type: "integer", nullable: false),
                    ManualPrimaryReason = table.Column<int>(type: "integer", nullable: true),
                    SecondaryReasonsJson = table.Column<string>(type: "text", nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Recommendation = table.Column<string>(type: "text", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false),
                    LastCustomerIntent = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    ReplyQualityScore = table.Column<int>(type: "integer", nullable: false),
                    FollowUpPriority = table.Column<int>(type: "integer", nullable: false),
                    NeedsFollowUp = table.Column<bool>(type: "boolean", nullable: false),
                    MissedOpportunity = table.Column<bool>(type: "boolean", nullable: false),
                    ManualNotes = table.Column<string>(type: "text", nullable: true),
                    CorrectedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Model = table.Column<string>(type: "text", nullable: false),
                    AnalysisVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSalesAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesIntelligenceDigests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowTimezone = table.Column<string>(type: "text", nullable: false),
                    DataFingerprint = table.Column<string>(type: "text", nullable: false),
                    ExecutiveSummary = table.Column<string>(type: "text", nullable: false),
                    FindingsJson = table.Column<string>(type: "text", nullable: false),
                    RecommendationsJson = table.Column<string>(type: "text", nullable: false),
                    RisksJson = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "text", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesIntelligenceDigests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSalesAnalyses_ProjectId_ConversationId",
                table: "ConversationSalesAnalyses",
                columns: new[] { "ProjectId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSalesAnalyses_ProjectId_ConversationStartedAtUtc",
                table: "ConversationSalesAnalyses",
                columns: new[] { "ProjectId", "ConversationStartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSalesAnalyses_ProjectId_NeedsFollowUp_FollowUpP~",
                table: "ConversationSalesAnalyses",
                columns: new[] { "ProjectId", "NeedsFollowUp", "FollowUpPriority" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesIntelligenceDigests_ProjectId_GeneratedAtUtc",
                table: "SalesIntelligenceDigests",
                columns: new[] { "ProjectId", "GeneratedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesIntelligenceDigests_ProjectId_WindowStartUtc_WindowEnd~",
                table: "SalesIntelligenceDigests",
                columns: new[] { "ProjectId", "WindowStartUtc", "WindowEndUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationSalesAnalyses");

            migrationBuilder.DropTable(
                name: "SalesIntelligenceDigests");
        }
    }
}
