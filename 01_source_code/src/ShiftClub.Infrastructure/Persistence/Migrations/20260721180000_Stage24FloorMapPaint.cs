using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

/// <summary>
/// Paint-style floor map: fill/stroke/font + branch background.
/// Additive only — does not touch existing element positions/labels.
/// </summary>
[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260721180000_Stage24FloorMapPaint")]
public partial class Stage24FloorMapPaint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "FloorBackgroundHex",
            table: "branches",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FillHex",
            table: "floor_map_elements",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "StrokeWidth",
            table: "floor_map_elements",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "FontSize",
            table: "floor_map_elements",
            type: "integer",
            nullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "Label",
            table: "floor_map_elements",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(120)",
            oldMaxLength: 120,
            oldNullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "FloorBackgroundHex", table: "branches");
        migrationBuilder.DropColumn(name: "FillHex", table: "floor_map_elements");
        migrationBuilder.DropColumn(name: "StrokeWidth", table: "floor_map_elements");
        migrationBuilder.DropColumn(name: "FontSize", table: "floor_map_elements");

        migrationBuilder.AlterColumn<string>(
            name: "Label",
            table: "floor_map_elements",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(500)",
            oldMaxLength: 500,
            oldNullable: true);
    }
}
