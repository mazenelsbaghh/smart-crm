using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFacebookMessengerCommentsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CommentsAiAutoReplyEnabled",
                table: "ProjectSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CommentsReplyDelay",
                table: "ProjectSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "MessengerAiAutoReplyEnabled",
                table: "ProjectSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MessengerReplyDelay",
                table: "ProjectSettings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FacebookCommentId",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookPostId",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentCommentId",
                table: "Messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookName",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacebookPSID",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Channel",
                table: "Conversations",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ConnectedPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacebookPageId = table.Column<string>(type: "text", nullable: false),
                    PageName = table.Column<string>(type: "text", nullable: false),
                    PageAccessToken = table.Column<string>(type: "text", nullable: false),
                    UserAccessToken = table.Column<string>(type: "text", nullable: true),
                    FacebookUserId = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectedPages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedPages_FacebookPageId",
                table: "ConnectedPages",
                column: "FacebookPageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConnectedPages_ProjectId",
                table: "ConnectedPages",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectedPages");

            migrationBuilder.DropColumn(
                name: "CommentsAiAutoReplyEnabled",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "CommentsReplyDelay",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "MessengerAiAutoReplyEnabled",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "MessengerReplyDelay",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "FacebookCommentId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FacebookPostId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "FacebookName",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "FacebookPSID",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Conversations");
        }
    }
}
