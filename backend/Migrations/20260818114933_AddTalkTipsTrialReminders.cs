using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTalkTipsTrialReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TalkTipsTrialReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstPromptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OneMinuteReminderSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FiveMinuteReminderSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DayReminderSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalkTipsTrialReminders", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TalkTipsTrialReminders");
        }
    }
}
