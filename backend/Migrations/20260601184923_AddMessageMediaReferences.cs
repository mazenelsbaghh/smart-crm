using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageMediaReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AssetId",
                table: "Messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Transcription",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_AssetId",
                table: "Messages",
                column: "AssetId");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Assets_AssetId",
                table: "Messages",
                column: "AssetId",
                principalTable: "Assets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Assets_AssetId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_AssetId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AssetId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Transcription",
                table: "Messages");
        }
    }
}
