using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvertisingOverviewQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_AdvertisingInsights_ProjectId_IsCurrent_IntervalStartUtc\" ON \"AdvertisingInsights\" (\"ProjectId\", \"IsCurrent\", \"IntervalStartUtc\");",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_AdvertisingConversions_ProjectId_OccurredAtUtc\" ON \"AdvertisingConversions\" (\"ProjectId\", \"OccurredAtUtc\");",
                suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_AdvertisingInsights_ProjectId_IsCurrent_IntervalStartUtc\";",
                suppressTransaction: true);
            migrationBuilder.Sql(
                "DROP INDEX CONCURRENTLY IF EXISTS \"IX_AdvertisingConversions_ProjectId_OccurredAtUtc\";",
                suppressTransaction: true);
        }
    }
}
