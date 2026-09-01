using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260830020000_AddGeminiAgentPlatformApiKey")]
public partial class AddGeminiAgentPlatformApiKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "GeminiAgentPlatformApiKey",
            table: "ProjectSettings",
            type: "text",
            nullable: false,
            defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "GeminiAgentPlatformApiKey",
            table: "ProjectSettings");
    }
}
