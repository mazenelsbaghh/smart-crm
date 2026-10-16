using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupStaffAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GroupAutomationAdminPhones",
                table: "ProjectSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "StaffSyncError",
                table: "GroupAppointments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StaffSyncedAtUtc",
                table: "GroupAppointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupervisorPhone",
                table: "GroupAppointments",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "GroupStaffNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupAppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    WhatsAppAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipientPhone = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProviderMessageId = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupStaffNotifications", x => x.Id);
                });
            migrationBuilder.CreateIndex("IX_GroupStaffNotifications_SentAtUtc_CancelledAtUtc_CreatedAt", "GroupStaffNotifications", new[] { "SentAtUtc", "CancelledAtUtc", "CreatedAt" });
            migrationBuilder.CreateIndex("IX_GroupStaffNotifications_ProjectId_GroupAppointmentId", "GroupStaffNotifications", new[] { "ProjectId", "GroupAppointmentId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupStaffNotifications");

            migrationBuilder.DropColumn(
                name: "GroupAutomationAdminPhones",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "StaffSyncError",
                table: "GroupAppointments");

            migrationBuilder.DropColumn(
                name: "StaffSyncedAtUtc",
                table: "GroupAppointments");

            migrationBuilder.DropColumn(
                name: "SupervisorPhone",
                table: "GroupAppointments");
        }
    }
}
