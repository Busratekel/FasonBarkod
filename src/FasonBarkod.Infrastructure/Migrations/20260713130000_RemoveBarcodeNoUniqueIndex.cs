using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FasonBarkod.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveBarcodeNoUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BarcodePrints_BarcodeNo",
                table: "BarcodePrints");

            migrationBuilder.CreateIndex(
                name: "IX_BarcodePrints_BarcodeNo",
                table: "BarcodePrints",
                column: "BarcodeNo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BarcodePrints_BarcodeNo",
                table: "BarcodePrints");

            migrationBuilder.CreateIndex(
                name: "IX_BarcodePrints_BarcodeNo",
                table: "BarcodePrints",
                column: "BarcodeNo",
                unique: true);
        }
    }
}
