using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowUpDeliveryEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SentAtUtc",
                table: "FollowUps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentForDueAtUtc",
                table: "FollowUps",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SentMessageId",
                table: "FollowUps",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SentAtUtc",
                table: "FollowUps");

            migrationBuilder.DropColumn(
                name: "SentForDueAtUtc",
                table: "FollowUps");

            migrationBuilder.DropColumn(
                name: "SentMessageId",
                table: "FollowUps");
        }
    }
}
