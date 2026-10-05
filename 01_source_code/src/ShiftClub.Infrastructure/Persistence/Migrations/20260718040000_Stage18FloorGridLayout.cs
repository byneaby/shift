using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260718040000_Stage18FloorGridLayout")]
public partial class Stage18FloorGridLayout : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "GridCol",
            table: "computers",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "GridRow",
            table: "computers",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Kind",
            table: "zones",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Hall");

        migrationBuilder.AddColumn<int>(
            name: "GridColumns",
            table: "zones",
            type: "integer",
            nullable: false,
            defaultValue: 6);

        migrationBuilder.AddColumn<int>(
            name: "GridRows",
            table: "zones",
            type: "integer",
            nullable: false,
            defaultValue: 4);

        migrationBuilder.Sql("""
            UPDATE computers
            SET "GridCol" = GREATEST(0, LEAST(20, FLOOR(COALESCE("MapX", 40) / 100.0)::int)),
                "GridRow" = GREATEST(0, LEAST(20, FLOOR(COALESCE("MapY", 40) / 100.0)::int))
            WHERE "IsApproved" = TRUE AND ("GridCol" IS NULL OR "GridRow" IS NULL);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GridCol", table: "computers");
        migrationBuilder.DropColumn(name: "GridRow", table: "computers");
        migrationBuilder.DropColumn(name: "Kind", table: "zones");
        migrationBuilder.DropColumn(name: "GridColumns", table: "zones");
        migrationBuilder.DropColumn(name: "GridRows", table: "zones");
    }
}
