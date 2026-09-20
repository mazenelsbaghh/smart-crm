using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddContentDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    SourceContent = table.Column<string>(type: "character varying(60000)", maxLength: 60000, nullable: false),
                    RequestedPageCount = table.Column<int>(type: "integer", nullable: false),
                    BrandLogoObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    BrandColorsJson = table.Column<string>(type: "text", nullable: false),
                    BrandStylePrompt = table.Column<string>(type: "text", nullable: false),
                    PlannerModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentDocuments", x => x.Id);
                    table.UniqueConstraint("AK_ContentDocuments_Id_ProjectId", x => new { x.Id, x.ProjectId });
                });

            migrationBuilder.CreateTable(
                name: "ContentDocumentPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageIndex = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    ImagePrompt = table.Column<string>(type: "text", nullable: false),
                    ImageObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ImageMimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentDocumentPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentDocumentPages_ContentDocuments_DocumentId_ProjectId",
                        columns: x => new { x.DocumentId, x.ProjectId },
                        principalTable: "ContentDocuments",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentDocumentPages_DocumentId_PageIndex",
                table: "ContentDocumentPages",
                columns: new[] { "DocumentId", "PageIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentDocumentPages_DocumentId_ProjectId",
                table: "ContentDocumentPages",
                columns: new[] { "DocumentId", "ProjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentDocuments_ProjectId_CreatedAt",
                table: "ContentDocuments",
                columns: new[] { "ProjectId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentDocumentPages");

            migrationBuilder.DropTable(
                name: "ContentDocuments");
        }
    }
}
