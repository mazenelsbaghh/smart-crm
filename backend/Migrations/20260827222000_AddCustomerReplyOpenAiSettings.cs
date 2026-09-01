using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260827222000_AddCustomerReplyOpenAiSettings")]
public partial class AddCustomerReplyOpenAiSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CustomerReplyModel",
            table: "ProjectSettings",
            type: "text",
            nullable: false,
            defaultValue: "gpt-5.6");

        migrationBuilder.AddColumn<string>(
            name: "CustomerReplyOpenAiApiKey",
            table: "ProjectSettings",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "CustomerReplyProvider",
            table: "ProjectSettings",
            type: "text",
            nullable: false,
            defaultValue: "Gemini");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CustomerReplyModel",
            table: "ProjectSettings");

        migrationBuilder.DropColumn(
            name: "CustomerReplyOpenAiApiKey",
            table: "ProjectSettings");

        migrationBuilder.DropColumn(
            name: "CustomerReplyProvider",
            table: "ProjectSettings");
    }
}
