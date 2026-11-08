using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDigests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Digests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    GeneratedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Digests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DigestItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DigestId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 600, nullable: false),
                    StyleTagsJson = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ImageUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigestItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigestItems_Digests_DigestId",
                        column: x => x.DigestId,
                        principalTable: "Digests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DigestItems_DigestId_SortOrder",
                table: "DigestItems",
                columns: new[] { "DigestId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_DigestItems_DigestId_Url",
                table: "DigestItems",
                columns: new[] { "DigestId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Digests_Date",
                table: "Digests",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DigestItems");

            migrationBuilder.DropTable(
                name: "Digests");
        }
    }
}
