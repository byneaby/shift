using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260915230000_Stage38TelegramCrm")]
public partial class Stage38TelegramCrm : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "customer_telegram_outreach",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Preview = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_customer_telegram_outreach", x => x.Id);
                table.ForeignKey(
                    name: "FK_customer_telegram_outreach_customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_customer_telegram_outreach_CustomerId_SentAt",
            table: "customer_telegram_outreach",
            columns: new[] { "CustomerId", "SentAt" });

        migrationBuilder.CreateIndex(
            name: "IX_customer_telegram_outreach_Kind_SentAt",
            table: "customer_telegram_outreach",
            columns: new[] { "Kind", "SentAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "customer_telegram_outreach");
    }
}
