using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260718120000_Stage22FloorMap")]
public partial class Stage22FloorMap : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "FloorGridCols",
            table: "branches",
            type: "integer",
            nullable: false,
            defaultValue: 24);

        migrationBuilder.AddColumn<int>(
            name: "FloorGridRows",
            table: "branches",
            type: "integer",
            nullable: false,
            defaultValue: 16);

        migrationBuilder.CreateTable(
            name: "floor_map_elements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                GridCol = table.Column<int>(type: "integer", nullable: false),
                GridRow = table.Column<int>(type: "integer", nullable: false),
                ColSpan = table.Column<int>(type: "integer", nullable: false),
                RowSpan = table.Column<int>(type: "integer", nullable: false),
                EndCol = table.Column<int>(type: "integer", nullable: true),
                EndRow = table.Column<int>(type: "integer", nullable: true),
                ColorHex = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsVisible = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_floor_map_elements", x => x.Id);
                table.ForeignKey(
                    name: "FK_floor_map_elements_branches_BranchId",
                    column: x => x.BranchId,
                    principalTable: "branches",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_floor_map_elements_BranchId",
            table: "floor_map_elements",
            column: "BranchId");

        // Seed a few starter landmarks on first branch (if empty)
        migrationBuilder.Sql("""
            INSERT INTO floor_map_elements ("Id", "BranchId", "Kind", "Label", "GridCol", "GridRow", "ColSpan", "RowSpan", "EndCol", "EndRow", "ColorHex", "SortOrder", "IsVisible", "CreatedAt")
            SELECT gen_random_uuid(), b."Id", 'Entrance', 'Вход', 0, 7, 2, 2, NULL, NULL, '#3ddc97', 1, TRUE, NOW()
            FROM branches b
            WHERE NOT EXISTS (SELECT 1 FROM floor_map_elements e WHERE e."BranchId" = b."Id")
            LIMIT 1;

            INSERT INTO floor_map_elements ("Id", "BranchId", "Kind", "Label", "GridCol", "GridRow", "ColSpan", "RowSpan", "EndCol", "EndRow", "ColorHex", "SortOrder", "IsVisible", "CreatedAt")
            SELECT gen_random_uuid(), b."Id", 'Cashier', 'Касса', 11, 0, 2, 1, NULL, NULL, '#ff8a2b', 2, TRUE, NOW()
            FROM branches b
            WHERE (SELECT COUNT(*) FROM floor_map_elements e WHERE e."BranchId" = b."Id") < 2
            LIMIT 1;

            INSERT INTO floor_map_elements ("Id", "BranchId", "Kind", "Label", "GridCol", "GridRow", "ColSpan", "RowSpan", "EndCol", "EndRow", "ColorHex", "SortOrder", "IsVisible", "CreatedAt")
            SELECT gen_random_uuid(), b."Id", 'Restroom', 'Уборная', 22, 14, 2, 2, NULL, NULL, '#a78bfa', 3, TRUE, NOW()
            FROM branches b
            WHERE (SELECT COUNT(*) FROM floor_map_elements e WHERE e."BranchId" = b."Id") < 3
            LIMIT 1;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "floor_map_elements");
        migrationBuilder.DropColumn(name: "FloorGridCols", table: "branches");
        migrationBuilder.DropColumn(name: "FloorGridRows", table: "branches");
    }
}
