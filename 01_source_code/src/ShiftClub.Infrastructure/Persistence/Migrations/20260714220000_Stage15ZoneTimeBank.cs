using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ShiftClubDbContext))]
    [Migration("20260714220000_Stage15ZoneTimeBank")]
    public partial class Stage15ZoneTimeBank : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_zone_time_banks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_zone_time_banks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_zone_time_banks_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_customer_zone_time_banks_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_zone_time_banks_CustomerId_ZoneId",
                table: "customer_zone_time_banks",
                columns: new[] { "CustomerId", "ZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_zone_time_banks_ZoneId",
                table: "customer_zone_time_banks",
                column: "ZoneId");

            migrationBuilder.AddColumn<Guid>(
                name: "ZoneId",
                table: "customer_time_bank_transactions",
                type: "uuid",
                nullable: true);

            // Перенос старых общих минут → первая зона филиала клиента
            migrationBuilder.Sql("""
                INSERT INTO customer_zone_time_banks ("Id", "CustomerId", "ZoneId", "Minutes", "CreatedAt")
                SELECT gen_random_uuid(), c."Id", z."Id", c."TimeBankMinutes", now()
                FROM customers c
                CROSS JOIN LATERAL (
                    SELECT z2."Id"
                    FROM zones z2
                    WHERE z2."BranchId" = c."BranchId" AND z2."IsActive"
                    ORDER BY z2."SortOrder", z2."Name"
                    LIMIT 1
                ) z
                WHERE c."TimeBankMinutes" > 0
                  AND NOT EXISTS (
                    SELECT 1 FROM customer_zone_time_banks b
                    WHERE b."CustomerId" = c."Id" AND b."ZoneId" = z."Id"
                  );
                """);

            migrationBuilder.Sql("""
                UPDATE customer_time_bank_transactions t
                SET "ZoneId" = z."Id"
                FROM customers c
                CROSS JOIN LATERAL (
                    SELECT z2."Id"
                    FROM zones z2
                    WHERE z2."BranchId" = c."BranchId" AND z2."IsActive"
                    ORDER BY z2."SortOrder", z2."Name"
                    LIMIT 1
                ) z
                WHERE t."CustomerId" = c."Id" AND t."ZoneId" IS NULL;
                """);

            migrationBuilder.Sql("""
                DELETE FROM customer_time_bank_transactions WHERE "ZoneId" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ZoneId",
                table: "customer_time_bank_transactions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_customer_time_bank_transactions_ZoneId",
                table: "customer_time_bank_transactions",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_customer_time_bank_transactions_CustomerId_ZoneId_CreatedAt",
                table: "customer_time_bank_transactions",
                columns: new[] { "CustomerId", "ZoneId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_customer_time_bank_transactions_zones_ZoneId",
                table: "customer_time_bank_transactions",
                column: "ZoneId",
                principalTable: "zones",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_customer_time_bank_transactions_zones_ZoneId",
                table: "customer_time_bank_transactions");

            migrationBuilder.DropIndex(
                name: "IX_customer_time_bank_transactions_CustomerId_ZoneId_CreatedAt",
                table: "customer_time_bank_transactions");

            migrationBuilder.DropIndex(
                name: "IX_customer_time_bank_transactions_ZoneId",
                table: "customer_time_bank_transactions");

            migrationBuilder.DropColumn(
                name: "ZoneId",
                table: "customer_time_bank_transactions");

            migrationBuilder.DropTable(
                name: "customer_zone_time_banks");
        }
    }
}
