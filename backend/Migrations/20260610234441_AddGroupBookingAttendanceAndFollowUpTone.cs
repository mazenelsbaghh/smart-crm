using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupBookingAttendanceAndFollowUpTone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAttended",
                table: "GroupAppointmentBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPaid",
                table: "GroupAppointmentBookings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Tone",
                table: "FollowUps",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAttended",
                table: "GroupAppointmentBookings");

            migrationBuilder.DropColumn(
                name: "IsPaid",
                table: "GroupAppointmentBookings");

            migrationBuilder.DropColumn(
                name: "Tone",
                table: "FollowUps");
        }
    }
}
