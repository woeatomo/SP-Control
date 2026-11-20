using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SparePartControl.Migrations
{
    /// <inheritdoc />
    public partial class ExcelMasterColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "InitialStock",
                table: "Parts",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "MaxStock",
                table: "Parts",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialStock",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "MaxStock",
                table: "Parts");
        }
    }
}
