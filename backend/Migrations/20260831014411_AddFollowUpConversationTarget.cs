using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260831014411_AddFollowUpConversationTarget")]
public partial class AddFollowUpConversationTarget : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Channel",
            table: "FollowUps",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "ConversationId",
            table: "FollowUps",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_FollowUps_ConversationId",
            table: "FollowUps",
            column: "ConversationId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_FollowUps_ConversationId",
            table: "FollowUps");

        migrationBuilder.DropColumn(
            name: "Channel",
            table: "FollowUps");

        migrationBuilder.DropColumn(
            name: "ConversationId",
            table: "FollowUps");
    }
}
