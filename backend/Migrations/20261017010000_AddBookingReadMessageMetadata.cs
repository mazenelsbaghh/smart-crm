using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261017010000_AddBookingReadMessageMetadata")]
public sealed class AddBookingReadMessageMetadata : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "WhatsAppRemoteJid", table: "Messages", type: "text", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ReceivedAtUtc", table: "Messages", type: "timestamp with time zone", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "WhatsAppRemoteJid", table: "Messages");
        migrationBuilder.DropColumn(name: "ReceivedAtUtc", table: "Messages");
    }
}
