using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SparePartControl.Migrations
{
    /// <inheritdoc />
    public partial class PartRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PartRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReqNo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    RequesterName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Line = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Urgency = table.Column<int>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    HandledBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResponseNote = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IssueDocNo = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartRequests_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PartRequestLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestId = table.Column<int>(type: "INTEGER", nullable: false),
                    PartId = table.Column<int>(type: "INTEGER", nullable: true),
                    FreeText = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Qty = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PartRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PartRequestLines_PartRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "PartRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PartRequestLines_Parts_PartId",
                        column: x => x.PartId,
                        principalTable: "Parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PartRequestLines_PartId",
                table: "PartRequestLines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequestLines_RequestId",
                table: "PartRequestLines",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequests_SiteId_Status",
                table: "PartRequests",
                columns: new[] { "SiteId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PartRequestLines");

            migrationBuilder.DropTable(
                name: "PartRequests");
        }
    }
}
