using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260916180000_Stage39CaseKeyBaseline")]
public partial class Stage39CaseKeyBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CasePlayBaselineMinutes",
            table: "customers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        // Баланс ключей = сумма ledger (исправляет расхождения вроде 18 при ledger +7).
        migrationBuilder.Sql(
            """
            UPDATE customers c
            SET "CaseKeysBalance" = COALESCE(
                (SELECT SUM(l."Delta") FROM case_key_ledger l WHERE l."CustomerId" = c."Id"),
                0);
            """);

        // Для уже получивших welcome — baseline = текущие минуты (без ретро-ключей за старые часы).
        migrationBuilder.Sql(
            """
            UPDATE customers c
            SET "CasePlayBaselineMinutes" = c."TotalMinutesPlayed";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CasePlayBaselineMinutes",
            table: "customers");
    }
}
