using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261004005500_AddOpenAiImageModel")]
public sealed class AddOpenAiImageModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>("OpenAiImageModel", "ContentAutomationSettings", "text",
            nullable: false, defaultValue: "gpt-image-2.5-sunburst");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn("OpenAiImageModel", "ContentAutomationSettings");
}
