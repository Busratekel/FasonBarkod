using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FasonBarkod.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodePrintVendorCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LabelType",
                table: "BarcodePrints",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LineNo",
                table: "BarcodePrints",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VendorCode",
                table: "BarcodePrints",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BarcodePrints_PrintDate",
                table: "BarcodePrints",
                column: "PrintDate");

            migrationBuilder.CreateIndex(
                name: "IX_BarcodePrints_VendorCode",
                table: "BarcodePrints",
                column: "VendorCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BarcodePrints_PrintDate",
                table: "BarcodePrints");

            migrationBuilder.DropIndex(
                name: "IX_BarcodePrints_VendorCode",
                table: "BarcodePrints");

            migrationBuilder.DropColumn(
                name: "LabelType",
                table: "BarcodePrints");

            migrationBuilder.DropColumn(
                name: "LineNo",
                table: "BarcodePrints");

            migrationBuilder.DropColumn(
                name: "VendorCode",
                table: "BarcodePrints");
        }
    }
}
