using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    [Migration("20260607162000_AddGeminiModelToProjectSettings")]
    public partial class AddGeminiModelToProjectSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeminiModel",
                table: "ProjectSettings",
                type: "text",
                nullable: false,
                defaultValue: "gemini-3.5-flash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeminiModel",
                table: "ProjectSettings");
        }
    }
}
