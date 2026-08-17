using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddQuranTikTokPublishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuranTikTokSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenId = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    ProtectedAccessToken = table.Column<string>(type: "text", nullable: true),
                    ProtectedRefreshToken = table.Column<string>(type: "text", nullable: true),
                    AccessTokenExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefreshTokenExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedScopes = table.Column<string>(type: "text", nullable: true),
                    LastPublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPublishId = table.Column<string>(type: "text", nullable: true),
                    LastPublishStatus = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuranTikTokSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuranTikTokSettings_ProjectId",
                table: "QuranTikTokSettings",
                column: "ProjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuranTikTokSettings");
        }
    }
}
