using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppGroupAutomationToProjectSettingsAndGroupAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupAutomationManagerPhone",
                table: "ProjectSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsWhatsAppGroupAutomationEnabled",
                table: "ProjectSettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppGroupInviteLink",
                table: "GroupAppointments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppGroupJid",
                table: "GroupAppointments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupAutomationManagerPhone",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "IsWhatsAppGroupAutomationEnabled",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "WhatsAppGroupInviteLink",
                table: "GroupAppointments");

            migrationBuilder.DropColumn(
                name: "WhatsAppGroupJid",
                table: "GroupAppointments");
        }
    }
}
