using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260718110000_Stage21SinglePcCustomerLogin")]
public partial class Stage21SinglePcCustomerLogin : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "LoggedInComputerId",
            table: "customers",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LoggedInAt",
            table: "customers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "LoginEpoch",
            table: "customers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateIndex(
            name: "IX_customers_LoggedInComputerId",
            table: "customers",
            column: "LoggedInComputerId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_customers_LoggedInComputerId",
            table: "customers");

        migrationBuilder.DropColumn(name: "LoginEpoch", table: "customers");
        migrationBuilder.DropColumn(name: "LoggedInAt", table: "customers");
        migrationBuilder.DropColumn(name: "LoggedInComputerId", table: "customers");
    }
}
