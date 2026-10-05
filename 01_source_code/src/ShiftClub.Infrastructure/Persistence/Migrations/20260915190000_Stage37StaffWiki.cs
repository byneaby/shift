using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260915190000_Stage37StaffWiki")]
public partial class Stage37StaffWiki : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "staff_wiki_pages",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                BodyMarkdown = table.Column<string>(type: "text", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                UpdatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_staff_wiki_pages", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_staff_wiki_pages_Slug",
            table: "staff_wiki_pages",
            column: "Slug",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_staff_wiki_pages_SortOrder",
            table: "staff_wiki_pages",
            column: "SortOrder");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "staff_wiki_pages");
    }
}
