using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddContentCardGameImageDesign : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageError",
                table: "ContentGameCards",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageMimeType",
                table: "ContentGameCards",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImageObjectKey",
                table: "ContentGameCards",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackImageMimeType",
                table: "ContentCardGames",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackImageObjectKey",
                table: "ContentCardGames",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DesignError",
                table: "ContentCardGames",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DesignStatus",
                table: "ContentCardGames",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Ready");

            migrationBuilder.AddColumn<string>(
                name: "ImageModel",
                table: "ContentCardGames",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageError",
                table: "ContentGameCards");

            migrationBuilder.DropColumn(
                name: "ImageMimeType",
                table: "ContentGameCards");

            migrationBuilder.DropColumn(
                name: "ImageObjectKey",
                table: "ContentGameCards");

            migrationBuilder.DropColumn(
                name: "BackImageMimeType",
                table: "ContentCardGames");

            migrationBuilder.DropColumn(
                name: "BackImageObjectKey",
                table: "ContentCardGames");

            migrationBuilder.DropColumn(
                name: "DesignError",
                table: "ContentCardGames");

            migrationBuilder.DropColumn(
                name: "DesignStatus",
                table: "ContentCardGames");

            migrationBuilder.DropColumn(
                name: "ImageModel",
                table: "ContentCardGames");
        }
    }
}
