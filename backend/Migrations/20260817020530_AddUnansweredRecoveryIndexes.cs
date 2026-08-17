using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUnansweredRecoveryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Messages_ConversationId_Timestamp_Id\" ON \"Messages\" (\"ConversationId\", \"Timestamp\" DESC, \"Id\" DESC);",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Conversations_Status_LastMessageTimestamp\" ON \"Conversations\" (\"Status\", \"LastMessageTimestamp\");",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_Messages_ConversationId_Timestamp_Id\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_Conversations_Status_LastMessageTimestamp\";",
                suppressTransaction: true);
        }
    }
}
