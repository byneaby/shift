using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ShiftClubDbContext))]
    [Migration("20260714184000_Stage13ComputerMacSoftDelete")]
    public partial class Stage13ComputerMacSoftDelete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "computers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_computers_MacAddress",
                table: "computers",
                column: "MacAddress");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_computers_MacAddress",
                table: "computers");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "computers");
        }
    }
}
