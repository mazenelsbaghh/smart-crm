using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20261016134209_AddWorkspaceQueryIndexes")]
    public partial class AddWorkspaceQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Deals_ProjectId_CustomerId"
                ON "Deals" ("ProjectId", "CustomerId")
                """, suppressTransaction: true);
            // Online builds keep message ingestion and operator writes available.
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_FollowUps_ProjectId_DueDate_Id"
                ON "FollowUps" ("ProjectId", "DueDate" DESC, "Id" DESC)
                """, suppressTransaction: true);
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_FollowUps_ProjectId_Status_DueDate"
                ON "FollowUps" ("ProjectId", "Status", "DueDate")
                """, suppressTransaction: true);
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Conversations_ProjectId_LastMessageTimestamp_Id"
                ON "Conversations" ("ProjectId", "LastMessageTimestamp" DESC, "Id" DESC)
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Deals_ProjectId_CustomerId\"", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_FollowUps_ProjectId_DueDate_Id\"", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_FollowUps_ProjectId_Status_DueDate\"", suppressTransaction: true);
            migrationBuilder.Sql("DROP INDEX CONCURRENTLY IF EXISTS \"IX_Conversations_ProjectId_LastMessageTimestamp_Id\"", suppressTransaction: true);
        }
    }
}
