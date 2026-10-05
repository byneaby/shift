using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ShiftClubDbContext))]
    [Migration("20260717103000_Stage17SessionConcurrency")]
    public partial class Stage17SessionConcurrency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX IF NOT EXISTS ix_gaming_sessions_one_live_per_computer
                ON gaming_sessions ("ComputerId")
                WHERE "Status" IN ('Active', 'Paused');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_gaming_sessions_one_live_per_computer;
                """);
        }
    }
}
