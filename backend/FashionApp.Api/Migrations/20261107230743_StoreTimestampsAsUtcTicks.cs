using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class StoreTimestampsAsUtcTicks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing databases store DateTimeOffset as ISO text. Convert those values
            // to UTC ticks before the column rebuild changes the affinity to INTEGER.
            // The cast is approximate (SQLite's julianday works in floating point), which
            // is acceptable for pre-release development data; fresh databases are empty.
            migrationBuilder.Sql(
                """
                UPDATE "Garments"
                SET "CreatedAt" = CASE WHEN typeof("CreatedAt") = 'text'
                        THEN CAST(ROUND((julianday("CreatedAt") - 1721425.5) * 864000000000.0) AS INTEGER)
                        ELSE "CreatedAt" END,
                    "UpdatedAt" = CASE WHEN typeof("UpdatedAt") = 'text'
                        THEN CAST(ROUND((julianday("UpdatedAt") - 1721425.5) * 864000000000.0) AS INTEGER)
                        ELSE "UpdatedAt" END
                WHERE typeof("CreatedAt") = 'text' OR typeof("UpdatedAt") = 'text';
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Outfits"
                SET "CreatedAt" = CASE WHEN typeof("CreatedAt") = 'text'
                        THEN CAST(ROUND((julianday("CreatedAt") - 1721425.5) * 864000000000.0) AS INTEGER)
                        ELSE "CreatedAt" END,
                    "UpdatedAt" = CASE WHEN typeof("UpdatedAt") = 'text'
                        THEN CAST(ROUND((julianday("UpdatedAt") - 1721425.5) * 864000000000.0) AS INTEGER)
                        ELSE "UpdatedAt" END
                WHERE typeof("CreatedAt") = 'text' OR typeof("UpdatedAt") = 'text';
                """);

            migrationBuilder.AlterColumn<long>(
                name: "UpdatedAt",
                table: "Outfits",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAt",
                table: "Outfits",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "UpdatedAt",
                table: "Garments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAt",
                table: "Garments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_Garments_CreatedAt_Id",
                table: "Garments",
                columns: new[] { "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Garments_CreatedAt_Id",
                table: "Garments");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Outfits",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Outfits",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Garments",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Garments",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            // Rebuild ISO text values so a downgraded database remains readable.
            migrationBuilder.Sql(
                """
                UPDATE "Garments"
                SET "CreatedAt" = CASE WHEN "CreatedAt" LIKE '%-%' THEN "CreatedAt"
                        ELSE strftime('%Y-%m-%d %H:%M:%f+00:00', ("CreatedAt" / 864000000000.0) + 1721425.5) END,
                    "UpdatedAt" = CASE WHEN "UpdatedAt" LIKE '%-%' THEN "UpdatedAt"
                        ELSE strftime('%Y-%m-%d %H:%M:%f+00:00', ("UpdatedAt" / 864000000000.0) + 1721425.5) END;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Outfits"
                SET "CreatedAt" = CASE WHEN "CreatedAt" LIKE '%-%' THEN "CreatedAt"
                        ELSE strftime('%Y-%m-%d %H:%M:%f+00:00', ("CreatedAt" / 864000000000.0) + 1721425.5) END,
                    "UpdatedAt" = CASE WHEN "UpdatedAt" LIKE '%-%' THEN "UpdatedAt"
                        ELSE strftime('%Y-%m-%d %H:%M:%f+00:00', ("UpdatedAt" / 864000000000.0) + 1721425.5) END;
                """);
        }
    }
}
