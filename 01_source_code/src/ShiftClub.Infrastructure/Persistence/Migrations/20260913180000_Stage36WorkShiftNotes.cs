using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260913180000_Stage36WorkShiftNotes")]
public partial class Stage36WorkShiftNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "BreakStartedAt",
            table: "work_shifts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "work_shift_notes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                WorkShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                AuthorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_work_shift_notes", x => x.Id);
                table.ForeignKey(
                    name: "FK_work_shift_notes_work_shifts_WorkShiftId",
                    column: x => x.WorkShiftId,
                    principalTable: "work_shifts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_work_shift_notes_CreatedAt",
            table: "work_shift_notes",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_work_shift_notes_WorkShiftId",
            table: "work_shift_notes",
            column: "WorkShiftId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "work_shift_notes");
        migrationBuilder.DropColumn(name: "BreakStartedAt", table: "work_shifts");
    }
}
