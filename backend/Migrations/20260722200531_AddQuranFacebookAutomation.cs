using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddQuranFacebookAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuranFacebookSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookPageId = table.Column<string>(type: "text", nullable: true),
                    PageName = table.Column<string>(type: "text", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IntervalHours = table.Column<int>(type: "integer", nullable: false),
                    CaptionTemplate = table.Column<string>(type: "text", nullable: false),
                    NextPublishAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastReelId = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuranFacebookSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuranFacebookSettings_ProjectId",
                table: "QuranFacebookSettings",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuranFacebookSettings");
        }
    }
}
