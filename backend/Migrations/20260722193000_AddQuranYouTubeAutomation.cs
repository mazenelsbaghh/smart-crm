using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260722193000_AddQuranYouTubeAutomation")]
public partial class AddQuranYouTubeAutomation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "QuranYouTubeSettings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ChannelId = table.Column<string>(type: "text", nullable: true),
                ChannelTitle = table.Column<string>(type: "text", nullable: true),
                ProtectedRefreshToken = table.Column<string>(type: "text", nullable: true),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                IntervalHours = table.Column<int>(type: "integer", nullable: false),
                PrivacyStatus = table.Column<string>(type: "text", nullable: false),
                CaptionTemplate = table.Column<string>(type: "text", nullable: false),
                NextPublishAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastPublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastVideoId = table.Column<string>(type: "text", nullable: true),
                LastError = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_QuranYouTubeSettings", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_QuranYouTubeSettings_ProjectId",
            table: "QuranYouTubeSettings",
            column: "ProjectId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "QuranYouTubeSettings");
    }
}
