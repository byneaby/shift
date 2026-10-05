using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260718100000_Stage20TariffTimeWindow")]
public partial class Stage20TariffTimeWindow : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DurationMode",
            table: "tariffs",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "FixedDuration");

        migrationBuilder.AddColumn<string>(
            name: "DurationMode",
            table: "gaming_sessions",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "FixedDuration");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "WindowPeriodStartsAt",
            table: "gaming_sessions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "WindowPeriodEndsAt",
            table: "gaming_sessions",
            type: "timestamp with time zone",
            nullable: true);

        // Day/night packages → TimeWindow (keep sale windows)
        migrationBuilder.Sql("""
            UPDATE tariffs
            SET "DurationMode" = 'TimeWindow',
                "FixedDurationMinutes" = NULL,
                "MinDurationMinutes" = NULL,
                "MaxDurationMinutes" = NULL
            WHERE "Code" LIKE '%_DAY' OR "Code" LIKE '%_NIGHT'
               OR lower("Name") IN ('день', 'ночь');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "WindowPeriodEndsAt", table: "gaming_sessions");
        migrationBuilder.DropColumn(name: "WindowPeriodStartsAt", table: "gaming_sessions");
        migrationBuilder.DropColumn(name: "DurationMode", table: "gaming_sessions");
        migrationBuilder.DropColumn(name: "DurationMode", table: "tariffs");
    }
}
