using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddContentCardGames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentCardGames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Brief = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Mechanic = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: false),
                    CardCount = table.Column<int>(type: "integer", nullable: false),
                    BrandLogoObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    BrandColorsJson = table.Column<string>(type: "text", nullable: false),
                    BrandStylePrompt = table.Column<string>(type: "text", nullable: false),
                    PlannerModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentCardGames", x => x.Id);
                    table.UniqueConstraint("AK_ContentCardGames_Id_ProjectId", x => new { x.Id, x.ProjectId });
                });

            migrationBuilder.CreateTable(
                name: "ContentGameCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    GameId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardIndex = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Prompt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Instruction = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentGameCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentGameCards_ContentCardGames_GameId_ProjectId",
                        columns: x => new { x.GameId, x.ProjectId },
                        principalTable: "ContentCardGames",
                        principalColumns: new[] { "Id", "ProjectId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentCardGames_ProjectId_CreatedAt",
                table: "ContentCardGames",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentGameCards_GameId_CardIndex",
                table: "ContentGameCards",
                columns: new[] { "GameId", "CardIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentGameCards_GameId_ProjectId",
                table: "ContentGameCards",
                columns: new[] { "GameId", "ProjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentGameCards");

            migrationBuilder.DropTable(
                name: "ContentCardGames");
        }
    }
}
