using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

/// <summary>
/// Перезапуск SHIFT CASE с 15.09.2026 (Asia/Almaty): обнуление ключей/истории, 1 welcome активным клиентам.
/// </summary>
[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260916183000_Stage40CaseKeyRelaunch")]
public partial class Stage40CaseKeyRelaunch : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DELETE FROM case_user_rewards;
            DELETE FROM case_openings;
            DELETE FROM case_key_ledger;

            UPDATE customers
            SET "CaseKeysBalance" = 0,
                "CasePlayBaselineMinutes" = "TotalMinutesPlayed";

            INSERT INTO case_key_ledger (
                "Id", "CustomerId", "Delta", "BalanceAfter", "Reason",
                "IdempotencyKey", "Comment", "CreatedAt")
            SELECT
                gen_random_uuid(),
                c."Id",
                1,
                1,
                1,
                'case-key:registration:' || c."Id"::text,
                'Welcome — перезапуск SHIFT CASE (15.09.2026)',
                TIMESTAMPTZ '2026-09-15 00:00:00+05'
            FROM customers c
            WHERE c."IsActive" = true AND NOT c."IsBlocked";

            UPDATE customers c
            SET "CaseKeysBalance" = 1
            WHERE c."IsActive" = true AND NOT c."IsBlocked";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Не восстанавливаем удалённые данные.
    }
}
