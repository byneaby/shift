using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage9Employees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HourlyRate",
                table: "employees",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MonthlySalary",
                table: "employees",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PayType",
                table: "employees",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ShiftRate",
                table: "employees",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "payroll_accruals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Basis = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApprovedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PaidAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_accruals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payroll_accruals_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedStart = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    PlannedEnd = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ActualStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BreakMinutes = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubstituteEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_shifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_work_shifts_employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_payroll_accruals_EmployeeId_PeriodFrom_PeriodTo",
                table: "payroll_accruals",
                columns: new[] { "EmployeeId", "PeriodFrom", "PeriodTo" });

            migrationBuilder.CreateIndex(
                name: "IX_work_shifts_BranchId_WorkDate",
                table: "work_shifts",
                columns: new[] { "BranchId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_work_shifts_EmployeeId_WorkDate",
                table: "work_shifts",
                columns: new[] { "EmployeeId", "WorkDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_accruals");

            migrationBuilder.DropTable(
                name: "work_shifts");

            migrationBuilder.DropColumn(
                name: "HourlyRate",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "MonthlySalary",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "PayType",
                table: "employees");

            migrationBuilder.DropColumn(
                name: "ShiftRate",
                table: "employees");
        }
    }
}
