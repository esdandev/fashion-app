using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FashionApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOutfitItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutfitItems",
                columns: table => new
                {
                    OutfitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GarmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Layer = table.Column<int>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutfitItems", x => new { x.OutfitId, x.GarmentId });
                    table.ForeignKey(
                        name: "FK_OutfitItems_Garments_GarmentId",
                        column: x => x.GarmentId,
                        principalTable: "Garments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutfitItems_Outfits_OutfitId",
                        column: x => x.OutfitId,
                        principalTable: "Outfits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutfitItems_GarmentId",
                table: "OutfitItems",
                column: "GarmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutfitItems_OutfitId_SortOrder",
                table: "OutfitItems",
                columns: new[] { "OutfitId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutfitItems");
        }
    }
}
