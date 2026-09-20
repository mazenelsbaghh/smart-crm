using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeExistingScheduleDemandLabels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "ConversationSalesAnalyses"
                SET "RequestedScheduleLabel" = CASE
                    WHEN lower("RequestedScheduleText" || ' ' || "RequestedScheduleLabel") ~ '(3|٣|ثلاث).*شه' THEN 'خلال ٣ شهور'
                    WHEN lower("RequestedScheduleText" || ' ' || "RequestedScheduleLabel") LIKE '%اسبوع%'
                      OR lower("RequestedScheduleText" || ' ' || "RequestedScheduleLabel") LIKE '%أسبوع%' THEN 'الأسبوع الجاي'
                    WHEN lower("RequestedScheduleText" || ' ' || "RequestedScheduleLabel") ~ 'شهر|سبتمبر|اكتوبر|أكتوبر|نوفمبر|ديسمبر|يناير|فبراير|مارس|ابريل|أبريل|مايو|يونيو|يوليو|اغسطس|أغسطس' THEN 'الشهر الجاي'
                    WHEN lower("RequestedScheduleText" || ' ' || "RequestedScheduleLabel") ~ 'صباح|ظهر' THEN 'من ١٢ إلى ٤'
                    WHEN lower("RequestedScheduleText" || ' ' || "RequestedScheduleLabel") ~ '(8|9|10|11|٨|٩|١٠|١١)' THEN 'من ٨ إلى ١٢'
                    WHEN lower("RequestedScheduleText" || ' ' || "RequestedScheduleLabel") ~ '(4|5|6|7|٤|٥|٦|٧)|مساء' THEN 'من ٤ إلى ٨'
                    ELSE 'أي وقت'
                END
                WHERE "RequestedScheduleText" <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Raw customer wording remains in RequestedScheduleText; normalized labels are intentionally not reversed.
        }
    }
}
