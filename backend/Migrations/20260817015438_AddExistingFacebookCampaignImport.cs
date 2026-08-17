using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddExistingFacebookCampaignImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BudgetOwnerExternalId",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BudgetOwnerType",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ImportedAtUtc",
                table: "ManagedAdvertisements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ManagementSource",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("UPDATE \"ManagedAdvertisements\" SET \"BudgetOwnerType\" = 'AdSet', \"ManagementSource\" = 'CreatedBySystem';");

            migrationBuilder.AlterColumn<string>(
                name: "BudgetOwnerType",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ManagementSource",
                table: "ManagedAdvertisements",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManagedAdvertisements_ProjectId_AdExternalId",
                table: "ManagedAdvertisements",
                columns: new[] { "ProjectId", "AdExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ManagedAdvertisements_ProjectId_AdExternalId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "BudgetOwnerExternalId",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "BudgetOwnerType",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "ImportedAtUtc",
                table: "ManagedAdvertisements");

            migrationBuilder.DropColumn(
                name: "ManagementSource",
                table: "ManagedAdvertisements");
        }
    }
}
