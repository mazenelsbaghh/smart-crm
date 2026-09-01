using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class HardenMultiWhatsAppDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WhatsAppAccountId",
                table: "GroupAppointments",
                type: "uuid",
                nullable: true);

            // Existing appointments were created before account selection existed,
            // so they must stay pinned to the legacy session (Id == ProjectId).
            migrationBuilder.Sql("""
                UPDATE "GroupAppointments" AS appointment
                SET "WhatsAppAccountId" = appointment."ProjectId"
                WHERE appointment."WhatsAppAccountId" IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "WhatsAppAccounts" AS account
                      WHERE account."Id" = appointment."ProjectId"
                        AND account."ProjectId" = appointment."ProjectId");
                """);

            // Keep one safety-preserving recipient before enforcing uniqueness.
            // DeliveryUnknown wins because retrying it could duplicate a provider send.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "CampaignId", "CustomerId"
                               ORDER BY CASE "Status"
                                   WHEN 8 THEN 100
                                   WHEN 5 THEN 90
                                   WHEN 3 THEN 80
                                   WHEN 2 THEN 70
                                   WHEN 1 THEN 60
                                   WHEN 4 THEN 50
                                   WHEN 7 THEN 40
                                   WHEN 6 THEN 30
                                   ELSE 0
                               END DESC,
                               "UpdatedAt" DESC,
                               "CreatedAt" DESC,
                               "Id"
                           ) AS row_number
                    FROM "CampaignRecipients"
                )
                DELETE FROM "CampaignRecipients" AS duplicate
                USING ranked
                WHERE duplicate."Id" = ranked."Id"
                  AND ranked.row_number > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_GroupAppointments_WhatsAppAccountId_ProjectId",
                table: "GroupAppointments",
                columns: new[] { "WhatsAppAccountId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignRecipients_CampaignId_CustomerId",
                table: "CampaignRecipients",
                columns: new[] { "CampaignId", "CustomerId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GroupAppointments_WhatsAppAccounts_WhatsAppAccountId_Projec~",
                table: "GroupAppointments",
                columns: new[] { "WhatsAppAccountId", "ProjectId" },
                principalTable: "WhatsAppAccounts",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupAppointments_WhatsAppAccounts_WhatsAppAccountId_Projec~",
                table: "GroupAppointments");

            migrationBuilder.DropIndex(
                name: "IX_GroupAppointments_WhatsAppAccountId_ProjectId",
                table: "GroupAppointments");

            migrationBuilder.DropIndex(
                name: "IX_CampaignRecipients_CampaignId_CustomerId",
                table: "CampaignRecipients");

            migrationBuilder.DropColumn(
                name: "WhatsAppAccountId",
                table: "GroupAppointments");
        }
    }
}
