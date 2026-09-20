using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleAvailabilityHorizon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AvailabilityHorizon",
                table: "ScheduleAvailabilityPreferences",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "AnyTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailabilityHorizon",
                table: "ScheduleAvailabilityPreferences");
        }
    }
}
