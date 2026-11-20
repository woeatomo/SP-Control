using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SparePartControl.Migrations
{
    /// <inheritdoc />
    public partial class MultiSite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockCheckSessions_Year_Semester",
                table: "StockCheckSessions");

            migrationBuilder.DropIndex(
                name: "IX_Parts_PartNumber",
                table: "Parts");

            migrationBuilder.AddColumn<bool>(
                name: "AllSites",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // user lama tetap punya akses semua site

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // data lama → site pertama

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "StockCheckSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // data lama → site pertama

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "Parts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // data lama → site pertama

            migrationBuilder.AddColumn<int>(
                name: "SiteId",
                table: "Loans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // data lama → site pertama

            migrationBuilder.CreateTable(
                name: "Sites",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CodePrefix = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserSites",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSites", x => new { x.UserId, x.SiteId });
                    table.ForeignKey(
                        name: "FK_UserSites_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserSites_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Site pertama: semua data yang sudah ada masuk ke sini. Nama & kode bisa diubah di menu Pengaturan → Site.
            migrationBuilder.Sql("INSERT INTO Sites (Id, Code, Name, CodePrefix, IsActive, CreatedAt) VALUES (1, 'SITE1', 'Site Utama', NULL, 1, datetime('now','localtime'));");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SiteId_DocNo",
                table: "Transactions",
                columns: new[] { "SiteId", "DocNo" });

            migrationBuilder.CreateIndex(
                name: "IX_StockCheckSessions_SiteId_Year_Semester",
                table: "StockCheckSessions",
                columns: new[] { "SiteId", "Year", "Semester" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parts_SiteId_PartNumber",
                table: "Parts",
                columns: new[] { "SiteId", "PartNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Loans_SiteId",
                table: "Loans",
                column: "SiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Sites_Code",
                table: "Sites",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSites_SiteId",
                table: "UserSites",
                column: "SiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Loans_Sites_SiteId",
                table: "Loans",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Parts_Sites_SiteId",
                table: "Parts",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockCheckSessions_Sites_SiteId",
                table: "StockCheckSessions",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Sites_SiteId",
                table: "Transactions",
                column: "SiteId",
                principalTable: "Sites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Loans_Sites_SiteId",
                table: "Loans");

            migrationBuilder.DropForeignKey(
                name: "FK_Parts_Sites_SiteId",
                table: "Parts");

            migrationBuilder.DropForeignKey(
                name: "FK_StockCheckSessions_Sites_SiteId",
                table: "StockCheckSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Sites_SiteId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "UserSites");

            migrationBuilder.DropTable(
                name: "Sites");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_SiteId_DocNo",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_StockCheckSessions_SiteId_Year_Semester",
                table: "StockCheckSessions");

            migrationBuilder.DropIndex(
                name: "IX_Parts_SiteId_PartNumber",
                table: "Parts");

            migrationBuilder.DropIndex(
                name: "IX_Loans_SiteId",
                table: "Loans");

            migrationBuilder.DropColumn(
                name: "AllSites",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "StockCheckSessions");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "SiteId",
                table: "Loans");

            migrationBuilder.CreateIndex(
                name: "IX_StockCheckSessions_Year_Semester",
                table: "StockCheckSessions",
                columns: new[] { "Year", "Semester" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Parts_PartNumber",
                table: "Parts",
                column: "PartNumber",
                unique: true);
        }
    }
}
