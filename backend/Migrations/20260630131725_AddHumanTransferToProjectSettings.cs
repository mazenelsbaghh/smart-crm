using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddHumanTransferToProjectSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HumanTransferEnabled",
                table: "ProjectSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "HumanTransferPhone",
                table: "ProjectSettings",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HumanTransferEnabled",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "HumanTransferPhone",
                table: "ProjectSettings");
        }
    }
}
