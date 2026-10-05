using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage10ClientLauncher : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "software_apps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Arguments = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WorkingDirectory = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IconPath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MinAge = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_software_apps", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_software_apps_BranchId_SortOrder",
                table: "software_apps",
                columns: new[] { "BranchId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "software_apps");
        }
    }
}
