using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260706111000_AddInstructorAndSessionScheduleToGroupAppointments")]
    /// <inheritdoc />
    public partial class AddInstructorAndSessionScheduleToGroupAppointments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActiveInstructors",
                table: "ProjectSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "CourseSecondDateTime",
                table: "GroupAppointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FreeSessionDateTime",
                table: "GroupAppointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstructorName",
                table: "GroupAppointments",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActiveInstructors",
                table: "ProjectSettings");

            migrationBuilder.DropColumn(
                name: "CourseSecondDateTime",
                table: "GroupAppointments");

            migrationBuilder.DropColumn(
                name: "FreeSessionDateTime",
                table: "GroupAppointments");

            migrationBuilder.DropColumn(
                name: "InstructorName",
                table: "GroupAppointments");
        }
    }
}
