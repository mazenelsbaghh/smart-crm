using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261016040000_AddAccessSessionFamilies")]
public sealed class AddAccessSessionFamilies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "SessionId", table: "RefreshTokens", type: "uuid", nullable: true);
        migrationBuilder.CreateIndex(name: "IX_RefreshTokens_SessionId", table: "RefreshTokens", column: "SessionId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_RefreshTokens_SessionId", table: "RefreshTokens");
        migrationBuilder.DropColumn(name: "SessionId", table: "RefreshTokens");
    }
}
