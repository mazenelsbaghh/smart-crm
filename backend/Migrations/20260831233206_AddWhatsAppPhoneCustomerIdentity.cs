using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppPhoneCustomerIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WhatsAppPhoneCustomerIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    NormalizedPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppPhoneCustomerIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhatsAppPhoneCustomerIdentities_Customers_CustomerId_Projec~",
                        columns: x => new { x.CustomerId, x.ProjectId },
                        principalTable: "Customers",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppPhoneCustomerIdentities_CustomerId_ProjectId",
                table: "WhatsAppPhoneCustomerIdentities",
                columns: new[] { "CustomerId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppPhoneCustomerIdentities_ProjectId_NormalizedPhone",
                table: "WhatsAppPhoneCustomerIdentities",
                columns: new[] { "ProjectId", "NormalizedPhone" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppPhoneCustomerIdentities");
        }
    }
}
