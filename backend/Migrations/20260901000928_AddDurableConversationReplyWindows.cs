using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDurableConversationReplyWindows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveAutomationSlotKey",
                table: "FollowUps",
                type: "character varying(220)",
                maxLength: 220,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConversationReplyWindows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConversationId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LatestIncomingMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    LatestIncomingVersion = table.Column<long>(type: "bigint", nullable: false),
                    LatestIncomingAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Sender = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    AggregatedContent = table.Column<string>(type: "text", nullable: false),
                    ChannelMetadata = table.Column<string>(type: "text", nullable: true),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RequiredWhatsAppConnectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppDeliveryIdempotencyKey = table.Column<string>(type: "character varying(220)", maxLength: 220, nullable: true),
                    DispatchedEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    DispatchedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationReplyWindows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversationReplyWindows_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConversationReplyWindows_Messages_LatestIncomingMessageId",
                        column: x => x.LatestIncomingMessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConversationReplyWindows_WhatsAppAccounts_WhatsAppAccountId~",
                        columns: x => new { x.WhatsAppAccountId, x.ProjectId },
                        principalTable: "WhatsAppAccounts",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_ProjectId_ActiveAutomationSlotKey",
                table: "FollowUps",
                columns: new[] { "ProjectId", "ActiveAutomationSlotKey" },
                unique: true,
                filter: "\"ActiveAutomationSlotKey\" IS NOT NULL AND \"Status\" IN ('Pending', 'Processing')");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReplyWindows_ConversationId",
                table: "ConversationReplyWindows",
                column: "ConversationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReplyWindows_DueAtUtc_DispatchedEventId",
                table: "ConversationReplyWindows",
                columns: new[] { "DueAtUtc", "DispatchedEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReplyWindows_LatestIncomingMessageId",
                table: "ConversationReplyWindows",
                column: "LatestIncomingMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationReplyWindows_WhatsAppAccountId_ProjectId",
                table: "ConversationReplyWindows",
                columns: new[] { "WhatsAppAccountId", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationReplyWindows");

            migrationBuilder.DropIndex(
                name: "IX_FollowUps_ProjectId_ActiveAutomationSlotKey",
                table: "FollowUps");

            migrationBuilder.DropColumn(
                name: "ActiveAutomationSlotKey",
                table: "FollowUps");
        }
    }
}
