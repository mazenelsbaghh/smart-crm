using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
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
