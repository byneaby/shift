using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260722123000_Stage28AccountProfileStaffTelegram")]
public partial class Stage28AccountProfileStaffTelegram : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Iin",
            table: "customers",
            type: "character varying(12)",
            maxLength: 12,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_customers_BranchId_Iin",
            table: "customers",
            columns: new[] { "BranchId", "Iin" },
            unique: true,
            filter: "\"Iin\" IS NOT NULL");

        migrationBuilder.AddColumn<string>(
            name: "FirstName",
            table: "employees",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "LastName",
            table: "employees",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "Iin",
            table: "employees",
            type: "character varying(12)",
            maxLength: 12,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "TelegramUserId",
            table: "employees",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "TelegramLinkedAt",
            table: "employees",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE employees
            SET "FirstName" = CASE
                    WHEN position(' ' in trim("DisplayName")) > 0
                        THEN left(trim("DisplayName"), position(' ' in trim("DisplayName")) - 1)
                    ELSE trim("DisplayName")
                END,
                "LastName" = CASE
                    WHEN position(' ' in trim("DisplayName")) > 0
                        THEN trim(substring(trim("DisplayName") from position(' ' in trim("DisplayName")) + 1))
                    ELSE ''
                END
            WHERE coalesce(trim("FirstName"), '') = '';
            """);

        migrationBuilder.CreateIndex(
            name: "IX_employees_TelegramUserId",
            table: "employees",
            column: "TelegramUserId");

        migrationBuilder.CreateIndex(
            name: "IX_employees_Iin",
            table: "employees",
            column: "Iin",
            unique: true,
            filter: "\"Iin\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_customers_BranchId_Iin", table: "customers");
        migrationBuilder.DropColumn(name: "Iin", table: "customers");

        migrationBuilder.DropIndex(name: "IX_employees_TelegramUserId", table: "employees");
        migrationBuilder.DropIndex(name: "IX_employees_Iin", table: "employees");
        migrationBuilder.DropColumn(name: "FirstName", table: "employees");
        migrationBuilder.DropColumn(name: "LastName", table: "employees");
        migrationBuilder.DropColumn(name: "Iin", table: "employees");
        migrationBuilder.DropColumn(name: "TelegramUserId", table: "employees");
        migrationBuilder.DropColumn(name: "TelegramLinkedAt", table: "employees");
    }
}
