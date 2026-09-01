using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260830190323_AddRequestedScheduleDemand")]
public partial class AddRequestedScheduleDemand : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "RequestedScheduleLabel",
            table: "ConversationSalesAnalyses",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "RequestedScheduleText",
            table: "ConversationSalesAnalyses",
            type: "text",
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "RequestedScheduleLabel",
            table: "ConversationSalesAnalyses");

        migrationBuilder.DropColumn(
            name: "RequestedScheduleText",
            table: "ConversationSalesAnalyses");
    }
}
