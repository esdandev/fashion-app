using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Garments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Subcategory = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ColorName = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ColorHex = table.Column<string>(type: "TEXT", maxLength: 7, nullable: false),
                    SecondaryColorsJson = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Pattern = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Seasons = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Formality = table.Column<int>(type: "INTEGER", nullable: false),
                    Material = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Brand = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    StyleTagsJson = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ThumbPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AnalysisJson = table.Column<string>(type: "TEXT", nullable: true),
                    Confidence = table.Column<double>(type: "REAL", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Garments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outfits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Occasion = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Source = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", maxLength: 600, nullable: true),
                    IsFavorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outfits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Garments_Category",
                table: "Garments",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Garments_ContentHash",
                table: "Garments",
                column: "ContentHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Garments_CreatedAt",
                table: "Garments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Garments_IsArchived",
                table: "Garments",
                column: "IsArchived");

            migrationBuilder.CreateIndex(
                name: "IX_Outfits_CreatedAt",
                table: "Outfits",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Outfits_IsFavorite",
                table: "Outfits",
                column: "IsFavorite");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Garments");

            migrationBuilder.DropTable(
                name: "Outfits");
        }
    }
}
