using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260903120000_Stage34StationKind")]
public partial class Stage34StationKind : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StationKind",
            table: "computers",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Pc");

        migrationBuilder.CreateIndex(
            name: "IX_computers_StationKind",
            table: "computers",
            column: "StationKind");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_computers_StationKind",
            table: "computers");

        migrationBuilder.DropColumn(
            name: "StationKind",
            table: "computers");
    }
}
