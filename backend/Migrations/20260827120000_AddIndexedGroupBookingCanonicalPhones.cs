using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shared.Infrastructure;

#nullable disable

namespace backend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260827120000_AddIndexedGroupBookingCanonicalPhones")]
public partial class AddIndexedGroupBookingCanonicalPhones : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE FUNCTION public.canonical_group_booking_phone_v1(phone text)
            RETURNS text
            LANGUAGE sql
            IMMUTABLE
            PARALLEL SAFE
            RETURNS NULL ON NULL INPUT
            AS $function$
                WITH translated AS (
                    SELECT translate(
                        btrim(phone),
                        '٠١٢٣٤٥٦٧٨٩۰۱۲۳۴۵۶۷۸۹',
                        '01234567890123456789') AS value
                ), normalized AS (
                    SELECT CASE
                        WHEN translate(
                            value,
                            '0123456789+()- ' || chr(9) || chr(10) || chr(11) || chr(12) || chr(13),
                            '') = ''
                            THEN translate(
                                value,
                                '()- ' || chr(9) || chr(10) || chr(11) || chr(12) || chr(13),
                                '')
                        ELSE NULL
                    END AS compact
                    FROM translated
                ), international AS (
                    SELECT CASE
                        WHEN compact LIKE '+%' THEN substring(compact FROM 2)
                        WHEN compact LIKE '00%' THEN substring(compact FROM 3)
                        ELSE compact
                    END AS digits
                    FROM normalized
                ), canonical AS (
                    SELECT CASE
                        WHEN length(digits) = 11 AND digits LIKE '01%' THEN '2' || digits
                        WHEN length(digits) = 10 AND digits LIKE '1%' THEN '20' || digits
                        ELSE digits
                    END AS digits
                    FROM international
                )
                SELECT CASE
                    WHEN digits ~ '^[1-9][0-9]{6,14}$' THEN digits
                    ELSE NULL
                END
                FROM canonical
            $function$;
            """);

        migrationBuilder.AddColumn<string>(
            name: "PhoneNumberCanonical",
            table: "Customers",
            type: "text",
            nullable: true,
            computedColumnSql: "public.canonical_group_booking_phone_v1(\"PhoneNumber\")",
            stored: true);

        migrationBuilder.AddColumn<string>(
            name: "CustomerPhoneCanonical",
            table: "GroupAppointmentBookings",
            type: "text",
            nullable: true,
            computedColumnSql: "public.canonical_group_booking_phone_v1(\"CustomerPhone\")",
            stored: true);

        migrationBuilder.CreateIndex(
            name: "IX_Customers_ProjectId_PhoneNumberCanonical",
            table: "Customers",
            columns: new[] { "ProjectId", "PhoneNumberCanonical" },
            filter: "\"PhoneNumberCanonical\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_GroupAppointmentBookings_ProjectId_CustomerId",
            table: "GroupAppointmentBookings",
            columns: new[] { "ProjectId", "CustomerId" });

        migrationBuilder.CreateIndex(
            name: "IX_GroupAppointmentBookings_ProjectId_CustomerPhoneCanonical",
            table: "GroupAppointmentBookings",
            columns: new[] { "ProjectId", "CustomerPhoneCanonical" },
            filter: "\"CustomerPhoneCanonical\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_Customers_ProjectId_PhoneNumberCanonical",
            table: "Customers");

        migrationBuilder.DropIndex(
            name: "IX_GroupAppointmentBookings_ProjectId_CustomerId",
            table: "GroupAppointmentBookings");

        migrationBuilder.DropIndex(
            name: "IX_GroupAppointmentBookings_ProjectId_CustomerPhoneCanonical",
            table: "GroupAppointmentBookings");

        migrationBuilder.DropColumn(
            name: "PhoneNumberCanonical",
            table: "Customers");

        migrationBuilder.DropColumn(
            name: "CustomerPhoneCanonical",
            table: "GroupAppointmentBookings");

        migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.canonical_group_booking_phone_v1(text);");
    }
}
