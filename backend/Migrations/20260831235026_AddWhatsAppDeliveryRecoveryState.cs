using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppDeliveryRecoveryState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DependsOnFollowUpId",
                table: "FollowUps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "WhatsAppDeliveryUnknownAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppDeliveryUnknownKey",
                table: "Conversations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DependsOnFollowUpId",
                table: "FollowUps");

            migrationBuilder.DropColumn(
                name: "WhatsAppDeliveryUnknownAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "WhatsAppDeliveryUnknownKey",
                table: "Conversations");
        }
    }
}
