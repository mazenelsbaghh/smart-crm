using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class RepairMessagingBookingIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SenderType",
                table: "Messages",
                type: "text",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddColumn<Guid>(
                name: "GroupAppointmentBookingId",
                table: "FollowUps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupAppointmentId",
                table: "FollowUps",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Messages" SET "SenderType" = CASE
                    WHEN "Direction" = 'Incoming' THEN 'Customer'
                    WHEN "ExternalMessageId" LIKE 'msg_ai\_%' ESCAPE '\' THEN 'AI'
                    WHEN "ExternalMessageId" LIKE 'msg_agent\_%' ESCAPE '\'
                      OR "ExternalMessageId" LIKE 'msg_out\_%' ESCAPE '\' THEN 'Agent'
                    ELSE 'Unknown' END;

                WITH matches AS (
                    SELECT f."Id", b."Id" AS booking_id, g."Id" AS group_id,
                        count(*) OVER (PARTITION BY f."Id") AS match_count
                    FROM "FollowUps" f
                    JOIN "GroupAppointmentBookings" b ON b."ProjectId" = f."ProjectId"
                        AND b."CustomerId" = f."CustomerId"
                    JOIN "GroupAppointments" g ON g."Id" = b."GroupAppointmentId"
                        AND g."ProjectId" = f."ProjectId" AND g."DateTime" = f."AppointmentTime"
                    WHERE f."Type" IN ('AppointmentReminder', 'Nurturing')
                      AND (f."WhatsAppAccountId" IS NULL OR f."WhatsAppAccountId" = g."WhatsAppAccountId")
                )
                UPDATE "FollowUps" f SET "GroupAppointmentBookingId" = m.booking_id,
                    "GroupAppointmentId" = m.group_id
                FROM matches m WHERE f."Id" = m."Id" AND m.match_count = 1;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SenderType",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "GroupAppointmentBookingId",
                table: "FollowUps");

            migrationBuilder.DropColumn(
                name: "GroupAppointmentId",
                table: "FollowUps");
        }
    }
}
