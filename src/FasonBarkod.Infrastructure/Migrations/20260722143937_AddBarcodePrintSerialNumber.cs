using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FasonBarkod.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBarcodePrintSerialNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "BarcodePrints",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "BarcodePrints");
        }
    }
}
