using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260813000000_AddCustomerWhatsAppLid")]
    public partial class AddCustomerWhatsAppLid : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WhatsAppLid",
                table: "Customers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_ProjectId_WhatsAppLid",
                table: "Customers",
                columns: new[] { "ProjectId", "WhatsAppLid" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_ProjectId_WhatsAppLid",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "WhatsAppLid",
                table: "Customers");
        }
    }
}
