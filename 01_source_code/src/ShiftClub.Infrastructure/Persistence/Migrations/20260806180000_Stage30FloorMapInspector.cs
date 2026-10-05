using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

/// <summary>
/// Floor map inspector: ShowIcon + RotationDeg for Photoshop-like element props.
/// </summary>
[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260806180000_Stage30FloorMapInspector")]
public partial class Stage30FloorMapInspector : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "ShowIcon",
            table: "floor_map_elements",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<int>(
            name: "RotationDeg",
            table: "floor_map_elements",
            type: "integer",
            nullable: false,
            defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShowIcon", table: "floor_map_elements");
        migrationBuilder.DropColumn(name: "RotationDeg", table: "floor_map_elements");
    }
}
