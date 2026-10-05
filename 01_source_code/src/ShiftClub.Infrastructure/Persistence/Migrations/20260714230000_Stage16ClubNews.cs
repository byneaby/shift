using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ShiftClubDbContext))]
    [Migration("20260714230000_Stage16ClubNews")]
    public partial class Stage16ClubNews : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "club_news_posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    PublishAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpireAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_club_news_posts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_club_news_posts_BranchId_IsPublished_IsPinned_SortOrder",
                table: "club_news_posts",
                columns: new[] { "BranchId", "IsPublished", "IsPinned", "SortOrder" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "club_news_posts");
        }
    }
}
