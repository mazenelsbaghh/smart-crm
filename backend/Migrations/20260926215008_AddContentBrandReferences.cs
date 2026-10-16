using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddContentBrandReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DesignSource",
                table: "ContentAutomationSettings",
                type: "text",
                nullable: false,
                defaultValue: "logo");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceColorsJson",
                table: "ContentAutomationSettings",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ReferenceImagesJson",
                table: "ContentAutomationSettings",
                type: "text",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesignSource",
                table: "ContentAutomationSettings");

            migrationBuilder.DropColumn(
                name: "ReferenceColorsJson",
                table: "ContentAutomationSettings");

            migrationBuilder.DropColumn(
                name: "ReferenceImagesJson",
                table: "ContentAutomationSettings");
        }
    }
}
