using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260830013500_AddTemporaryGeminiModelOverride")]
public partial class AddTemporaryGeminiModelOverride : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TemporaryGeminiModel",
            table: "ProjectSettings",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "TemporaryGeminiModelExpiresAtUtc",
            table: "ProjectSettings",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TemporaryGeminiModel", table: "ProjectSettings");
        migrationBuilder.DropColumn(name: "TemporaryGeminiModelExpiresAtUtc", table: "ProjectSettings");
    }
}
