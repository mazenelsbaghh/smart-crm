using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledReplyReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasUnresolvedReplyIssue",
                table: "ConversationSalesAnalyses",
                type: "boolean",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReplyReviewCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    State = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Phase = table.Column<string>(type: "text", nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceFollowUpAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifyAfterMessageAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRunAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DispatchUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LeaseUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NeedsResolution = table.Column<bool>(type: "boolean", nullable: false),
                    QualityScore = table.Column<int>(type: "integer", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Recommendation = table.Column<string>(type: "text", nullable: false),
                    DraftContent = table.Column<string>(type: "text", nullable: false),
                    DraftBasedOnMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    DraftGeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyReviewCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplyReviewCases_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplyReviewSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PrepareDrafts = table.Column<bool>(type: "boolean", nullable: false),
                    IntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    QuietMinutes = table.Column<int>(type: "integer", nullable: false),
                    VerifyAfterMinutes = table.Column<int>(type: "integer", nullable: false),
                    BatchSize = table.Column<int>(type: "integer", nullable: false),
                    NextScanAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastScanAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyReviewSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReplyReviewRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    QualityScore = table.Column<int>(type: "integer", nullable: true),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplyReviewRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplyReviewRuns_ReplyReviewCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "ReplyReviewCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyReviewCases_ConversationId",
                table: "ReplyReviewCases",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyReviewCases_ProjectId_ConversationId",
                table: "ReplyReviewCases",
                columns: new[] { "ProjectId", "ConversationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplyReviewCases_State_NextRunAtUtc",
                table: "ReplyReviewCases",
                columns: new[] { "State", "NextRunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyReviewRuns_CaseId",
                table: "ReplyReviewRuns",
                column: "CaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplyReviewRuns_ProjectId_CaseId_FinishedAtUtc",
                table: "ReplyReviewRuns",
                columns: new[] { "ProjectId", "CaseId", "FinishedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReplyReviewSchedules_ProjectId",
                table: "ReplyReviewSchedules",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplyReviewRuns");

            migrationBuilder.DropTable(
                name: "ReplyReviewSchedules");

            migrationBuilder.DropTable(
                name: "ReplyReviewCases");

            migrationBuilder.DropColumn(
                name: "HasUnresolvedReplyIssue",
                table: "ConversationSalesAnalyses");
        }
    }
}
