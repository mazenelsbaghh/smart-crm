using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiWhatsAppAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WhatsAppAccountId",
                table: "FollowUps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WhatsAppAccountId",
                table: "Conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WhatsAppDestinationId",
                table: "Conversations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUnansweredRecoveryAttemptAt",
                table: "Conversations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WhatsAppAccountId",
                table: "Campaigns",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WhatsAppAccountId",
                table: "AdvertisingWhatsAppDestinations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Customers_Id_ProjectId",
                table: "Customers",
                columns: new[] { "Id", "ProjectId" });

            migrationBuilder.CreateTable(
                name: "WhatsAppAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppAccounts", x => x.Id);
                    table.UniqueConstraint("AK_WhatsAppAccounts_Id_ProjectId", x => new { x.Id, x.ProjectId });
                });

            // The stable legacy account deliberately reuses the project id. That
            // preserves every existing Baileys credential directory and makes the
            // rollout additive for callers that did not yet send an account id.
            migrationBuilder.Sql("""
                INSERT INTO "WhatsAppAccounts"
                    ("Id", "ProjectId", "Name", "IsDefault", "CreatedAt", "UpdatedAt")
                SELECT p."Id", p."Id", 'واتساب الرئيسي', TRUE, NOW(), NOW()
                FROM "Projects" p
                ON CONFLICT ("Id") DO NOTHING;

                UPDATE "Conversations"
                SET "WhatsAppAccountId" = "ProjectId"
                WHERE "Channel" = 'WhatsApp' AND "WhatsAppAccountId" IS NULL;

                UPDATE "Conversations" c
                SET "WhatsAppDestinationId" = cloud."DestinationId",
                    "WhatsAppAccountId" = NULL
                FROM (
                    SELECT DISTINCT ON (o."ConversationId")
                           o."ConversationId", o."DestinationId"
                    FROM "AdvertisingAttributionObservations" o
                    WHERE o."GatewayType" = 'CloudApi'
                    ORDER BY o."ConversationId", o."MessageOccurredAtUtc" DESC, o."Id"
                ) cloud
                WHERE c."Id" = cloud."ConversationId";

                UPDATE "FollowUps" f
                SET "ConversationId" = c."Id",
                    "Channel" = COALESCE(f."Channel", c."Channel"),
                    "WhatsAppAccountId" = c."WhatsAppAccountId"
                FROM "Conversations" c
                WHERE f."ConversationId" = c."Id"
                  AND c."Channel" = 'WhatsApp'
                  AND f."WhatsAppAccountId" IS NULL;

                UPDATE "FollowUps" f
                SET "Channel" = c."Channel"
                FROM "Conversations" c
                WHERE f."ConversationId" = c."Id"
                  AND f."Channel" IS NULL;

                UPDATE "FollowUps"
                SET "WhatsAppAccountId" = "ProjectId"
                WHERE "WhatsAppAccountId" IS NULL
                  AND "Channel" = 'WhatsApp'
                  AND NOT EXISTS (
                      SELECT 1
                      FROM "Conversations" c
                      WHERE c."Id" = "FollowUps"."ConversationId"
                        AND c."WhatsAppDestinationId" IS NOT NULL
                  );

                UPDATE "FollowUps" f
                SET "WhatsAppAccountId" = f."ProjectId",
                    "Channel" = 'WhatsApp'
                FROM "Customers" c
                WHERE f."CustomerId" = c."Id"
                  AND f."ProjectId" = c."ProjectId"
                  AND f."WhatsAppAccountId" IS NULL
                  AND f."Channel" IS NULL
                  AND (f."ConversationId" IS NULL OR NOT EXISTS (
                      SELECT 1
                      FROM "Conversations" linked
                      WHERE linked."Id" = f."ConversationId"
                        AND linked."WhatsAppDestinationId" IS NOT NULL
                  ))
                  AND c."PhoneNumber" IS NOT NULL
                  AND BTRIM(c."PhoneNumber") <> '';

                UPDATE "Campaigns"
                SET "WhatsAppAccountId" = "ProjectId"
                WHERE "WhatsAppAccountId" IS NULL;

                UPDATE "AdvertisingWhatsAppDestinations"
                SET "WhatsAppAccountId" = "ProjectId"
                WHERE "WhatsAppAccountId" IS NULL
                  AND "WhatsAppIntegrationMode" = 2;
                """);

            migrationBuilder.CreateTable(
                name: "WhatsAppCustomerIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppCustomerIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppCustomerIdentities_Customers_CustomerId_ProjectId",
                        columns: x => new { x.CustomerId, x.ProjectId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WhatsAppCustomerIdentities_WhatsAppAccounts_WhatsAppAccount~",
                        columns: x => new { x.WhatsAppAccountId, x.ProjectId },
                        principalTable: "WhatsAppAccounts",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "WhatsAppCustomerIdentities"
                    ("Id", "ProjectId", "WhatsAppAccountId", "CustomerId", "ExternalId", "Kind", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), legacy."ProjectId", legacy."ProjectId", legacy."Id",
                       legacy."WhatsAppLid", 'Lid', NOW(), NOW()
                FROM (
                    SELECT DISTINCT ON (c."ProjectId", c."WhatsAppLid")
                           c."Id", c."ProjectId", c."WhatsAppLid"
                    FROM "Customers" c
                    WHERE c."WhatsAppLid" IS NOT NULL
                      AND BTRIM(c."WhatsAppLid") <> ''
                    ORDER BY c."ProjectId", c."WhatsAppLid", c."UpdatedAt" DESC, c."Id"
                ) legacy;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_WhatsAppAccountId_ProjectId",
                table: "FollowUps",
                columns: new[] { "WhatsAppAccountId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ProjectId_CustomerId_Channel_WhatsAppAccountI~",
                table: "Conversations",
                columns: new[] { "ProjectId", "CustomerId", "Channel", "WhatsAppAccountId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_WhatsAppAccountId_ProjectId",
                table: "Conversations",
                columns: new[] { "WhatsAppAccountId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ProjectId_WhatsAppDestinationId",
                table: "Conversations",
                columns: new[] { "ProjectId", "WhatsAppDestinationId" });

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_WhatsAppAccountId_ProjectId",
                table: "Campaigns",
                columns: new[] { "WhatsAppAccountId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AdvertisingWhatsAppDestinations_WhatsAppAccountId_ProjectId",
                table: "AdvertisingWhatsAppDestinations",
                columns: new[] { "WhatsAppAccountId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppAccounts_ProjectId_IsDefault",
                table: "WhatsAppAccounts",
                columns: new[] { "ProjectId", "IsDefault" },
                unique: true,
                filter: "\"IsDefault\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppCustomerIdentities_CustomerId_ProjectId",
                table: "WhatsAppCustomerIdentities",
                columns: new[] { "CustomerId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppCustomerIdentities_WhatsAppAccountId_ExternalId",
                table: "WhatsAppCustomerIdentities",
                columns: new[] { "WhatsAppAccountId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppCustomerIdentities_WhatsAppAccountId_ProjectId",
                table: "WhatsAppCustomerIdentities",
                columns: new[] { "WhatsAppAccountId", "ProjectId" });

            migrationBuilder.AddForeignKey(
                name: "FK_AdvertisingWhatsAppDestinations_WhatsAppAccounts_WhatsAppAc~",
                table: "AdvertisingWhatsAppDestinations",
                columns: new[] { "WhatsAppAccountId", "ProjectId" },
                principalTable: "WhatsAppAccounts",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Campaigns_WhatsAppAccounts_WhatsAppAccountId_ProjectId",
                table: "Campaigns",
                columns: new[] { "WhatsAppAccountId", "ProjectId" },
                principalTable: "WhatsAppAccounts",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_WhatsAppAccounts_WhatsAppAccountId_ProjectId",
                table: "Conversations",
                columns: new[] { "WhatsAppAccountId", "ProjectId" },
                principalTable: "WhatsAppAccounts",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FollowUps_WhatsAppAccounts_WhatsAppAccountId_ProjectId",
                table: "FollowUps",
                columns: new[] { "WhatsAppAccountId", "ProjectId" },
                principalTable: "WhatsAppAccounts",
                principalColumns: new[] { "Id", "ProjectId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AdvertisingWhatsAppDestinations_WhatsAppAccounts_WhatsAppAc~",
                table: "AdvertisingWhatsAppDestinations");

            migrationBuilder.DropForeignKey(
                name: "FK_Campaigns_WhatsAppAccounts_WhatsAppAccountId_ProjectId",
                table: "Campaigns");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_WhatsAppAccounts_WhatsAppAccountId_ProjectId",
                table: "Conversations");

            migrationBuilder.DropForeignKey(
                name: "FK_FollowUps_WhatsAppAccounts_WhatsAppAccountId_ProjectId",
                table: "FollowUps");

            migrationBuilder.DropTable(
                name: "WhatsAppCustomerIdentities");

            migrationBuilder.DropTable(
                name: "WhatsAppAccounts");

            migrationBuilder.DropIndex(
                name: "IX_FollowUps_WhatsAppAccountId_ProjectId",
                table: "FollowUps");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Customers_Id_ProjectId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_ProjectId_CustomerId_Channel_WhatsAppAccountI~",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_WhatsAppAccountId_ProjectId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_ProjectId_WhatsAppDestinationId",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Campaigns_WhatsAppAccountId_ProjectId",
                table: "Campaigns");

            migrationBuilder.DropIndex(
                name: "IX_AdvertisingWhatsAppDestinations_WhatsAppAccountId_ProjectId",
                table: "AdvertisingWhatsAppDestinations");

            migrationBuilder.DropColumn(
                name: "WhatsAppAccountId",
                table: "FollowUps");

            migrationBuilder.DropColumn(
                name: "WhatsAppAccountId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "WhatsAppDestinationId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "LastUnansweredRecoveryAttemptAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "WhatsAppAccountId",
                table: "Campaigns");

            migrationBuilder.DropColumn(
                name: "WhatsAppAccountId",
                table: "AdvertisingWhatsAppDestinations");
        }
    }
}
