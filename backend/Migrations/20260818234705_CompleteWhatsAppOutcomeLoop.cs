using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class CompleteWhatsAppOutcomeLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreviousProtectedSigningSecret",
                table: "AdvertisingWebhookSources",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ConversionId",
                table: "AdvertisingAttributionTouches",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousProtectedSigningSecret",
                table: "AdvertisingWebhookSources");

            migrationBuilder.AlterColumn<Guid>(
                name: "ConversionId",
                table: "AdvertisingAttributionTouches",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
