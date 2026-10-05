using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(ShiftClubDbContext))]
    [Migration("20260714210000_Stage14TimeBankLedger")]
    public partial class Stage14TimeBankLedger : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_time_bank_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    BalanceBefore = table.Column<int>(type: "integer", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    GamingSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_time_bank_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_time_bank_transactions_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_time_bank_transactions_CustomerId_CreatedAt",
                table: "customer_time_bank_transactions",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_time_bank_transactions_IdempotencyKey",
                table: "customer_time_bank_transactions",
                column: "IdempotencyKey");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_time_bank_transactions");
        }
    }
}
