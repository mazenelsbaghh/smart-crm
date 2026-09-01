using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260822153000_AddQuranTikTokAutomation")]
public partial class AddQuranTikTokAutomation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>("AllowComment", "QuranTikTokSettings", "boolean", nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<bool>("AllowDuet", "QuranTikTokSettings", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>("AllowStitch", "QuranTikTokSettings", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>("CaptionTemplate", "QuranTikTokSettings", "text", nullable: false,
            defaultValue: Modules.QuranChallenge.Domain.QuranTikTokSettings.DefaultCaption);
        migrationBuilder.AddColumn<int>("IntervalHours", "QuranTikTokSettings", "integer", nullable: false, defaultValue: 4);
        migrationBuilder.AddColumn<bool>("IsEnabled", "QuranTikTokSettings", "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<DateTime>("NextPublishAtUtc", "QuranTikTokSettings", "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>("PrivacyLevel", "QuranTikTokSettings", "text", nullable: false,
            defaultValue: "PUBLIC_TO_EVERYONE");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("AllowComment", "QuranTikTokSettings");
        migrationBuilder.DropColumn("AllowDuet", "QuranTikTokSettings");
        migrationBuilder.DropColumn("AllowStitch", "QuranTikTokSettings");
        migrationBuilder.DropColumn("CaptionTemplate", "QuranTikTokSettings");
        migrationBuilder.DropColumn("IntervalHours", "QuranTikTokSettings");
        migrationBuilder.DropColumn("IsEnabled", "QuranTikTokSettings");
        migrationBuilder.DropColumn("NextPublishAtUtc", "QuranTikTokSettings");
        migrationBuilder.DropColumn("PrivacyLevel", "QuranTikTokSettings");
    }
}
