using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SparePartControl.Migrations
{
    /// <inheritdoc />
    public partial class MachineMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActivityId",
                table: "Transactions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ActivityId",
                table: "PartRequests",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "PartRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Machines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false, collation: "NOCASE"),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Area = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Maker = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    ModelNo = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    PmRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Machines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Machines_Sites_SiteId",
                        column: x => x.SiteId,
                        principalTable: "Sites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaintenancePlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Shift = table.Column<int>(type: "INTEGER", nullable: true),
                    MachineId = table.Column<int>(type: "INTEGER", nullable: true),
                    Task = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Pic = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Cancelled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenancePlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenancePlans_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceActivities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SiteId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Shift = table.Column<int>(type: "INTEGER", nullable: false),
                    MachineId = table.Column<int>(type: "INTEGER", nullable: true),
                    MachineText = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Problem = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Cause = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    StartTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    EndTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    MachineStopped = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Technicians = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    HasPartReplacement = table.Column<bool>(type: "INTEGER", nullable: false),
                    Remarks = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PlanId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivities_Machines_MachineId",
                        column: x => x.MachineId,
                        principalTable: "Machines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MaintenanceActivities_MaintenancePlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "MaintenancePlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ActivityId",
                table: "Transactions",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PartRequests_ActivityId",
                table: "PartRequests",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_Machines_SiteId_Code",
                table: "Machines",
                columns: new[] { "SiteId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivities_MachineId_Date",
                table: "MaintenanceActivities",
                columns: new[] { "MachineId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivities_PlanId",
                table: "MaintenanceActivities",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenanceActivities_SiteId_Date_Shift",
                table: "MaintenanceActivities",
                columns: new[] { "SiteId", "Date", "Shift" });

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_MachineId",
                table: "MaintenancePlans",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_MaintenancePlans_SiteId_Date",
                table: "MaintenancePlans",
                columns: new[] { "SiteId", "Date" });

            migrationBuilder.AddForeignKey(
                name: "FK_PartRequests_MaintenanceActivities_ActivityId",
                table: "PartRequests",
                column: "ActivityId",
                principalTable: "MaintenanceActivities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_MaintenanceActivities_ActivityId",
                table: "Transactions",
                column: "ActivityId",
                principalTable: "MaintenanceActivities",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PartRequests_MaintenanceActivities_ActivityId",
                table: "PartRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_MaintenanceActivities_ActivityId",
                table: "Transactions");

            migrationBuilder.DropTable(
                name: "MaintenanceActivities");

            migrationBuilder.DropTable(
                name: "MaintenancePlans");

            migrationBuilder.DropTable(
                name: "Machines");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_ActivityId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_PartRequests_ActivityId",
                table: "PartRequests");

            migrationBuilder.DropColumn(
                name: "ActivityId",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "ActivityId",
                table: "PartRequests");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "PartRequests");
        }
    }
}
