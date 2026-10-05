using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260718060000_Stage19FinanceUpgrade")]
public partial class Stage19FinanceUpgrade : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "SalesKaspi",
            table: "cash_shifts",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "SalesTransfer",
            table: "cash_shifts",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "RefundsCard",
            table: "cash_shifts",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "RefundsKaspi",
            table: "cash_shifts",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "RefundsTransfer",
            table: "cash_shifts",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "RefundsOther",
            table: "cash_shifts",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<decimal>(
            name: "DepositsTotal",
            table: "cash_shifts",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SalesKaspi", table: "cash_shifts");
        migrationBuilder.DropColumn(name: "SalesTransfer", table: "cash_shifts");
        migrationBuilder.DropColumn(name: "RefundsCard", table: "cash_shifts");
        migrationBuilder.DropColumn(name: "RefundsKaspi", table: "cash_shifts");
        migrationBuilder.DropColumn(name: "RefundsTransfer", table: "cash_shifts");
        migrationBuilder.DropColumn(name: "RefundsOther", table: "cash_shifts");
        migrationBuilder.DropColumn(name: "DepositsTotal", table: "cash_shifts");
    }
}
