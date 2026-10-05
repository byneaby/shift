using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

/// <summary>
/// Loyalty admin: time discount % on levels + manual level lock on customers.
/// Additive only.
/// </summary>
[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260722090000_Stage25LoyaltyAdmin")]
public partial class Stage25LoyaltyAdmin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "TimeDiscountPercent",
            table: "loyalty_levels",
            type: "numeric(18,2)",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);

        migrationBuilder.AddColumn<bool>(
            name: "LoyaltyLevelLocked",
            table: "customers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Seed sensible defaults: mirror deposit bonus as time discount where unset.
        migrationBuilder.Sql(
            """
            UPDATE loyalty_levels
            SET "TimeDiscountPercent" = "BonusPercent"
            WHERE "TimeDiscountPercent" = 0 AND "BonusPercent" > 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TimeDiscountPercent", table: "loyalty_levels");
        migrationBuilder.DropColumn(name: "LoyaltyLevelLocked", table: "customers");
    }
}
