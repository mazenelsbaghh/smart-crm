using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261004003000_AddContentImageProvider")]
public sealed class AddContentImageProvider : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("ImageProvider", "ContentAutomationSettings", "text",
            nullable: false, defaultValue: "Gemini");
        migrationBuilder.AddColumn<string>("OpenAiImageApiKey", "ContentAutomationSettings", "text",
            nullable: false, defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("ImageProvider", "ContentAutomationSettings");
        migrationBuilder.DropColumn("OpenAiImageApiKey", "ContentAutomationSettings");
    }
}
