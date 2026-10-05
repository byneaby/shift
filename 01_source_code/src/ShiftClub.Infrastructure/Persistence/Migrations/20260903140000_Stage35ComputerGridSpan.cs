using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260903140000_Stage35ComputerGridSpan")]
public partial class Stage35ComputerGridSpan : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "GridColSpan",
            table: "computers",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<int>(
            name: "GridRowSpan",
            table: "computers",
            type: "integer",
            nullable: false,
            defaultValue: 1);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GridColSpan", table: "computers");
        migrationBuilder.DropColumn(name: "GridRowSpan", table: "computers");
    }
}
