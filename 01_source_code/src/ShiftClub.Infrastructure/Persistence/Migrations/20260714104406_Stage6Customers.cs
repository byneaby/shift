using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage6Customers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loyalty_levels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MinSpent = table.Column<int>(type: "integer", nullable: false),
                    BonusPercent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_levels", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PinHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Balance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BonusBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LoyaltyLevelId = table.Column<Guid>(type: "uuid", nullable: true),
                    VisitCount = table.Column<int>(type: "integer", nullable: false),
                    TotalMinutesPlayed = table.Column<int>(type: "integer", nullable: false),
                    TotalSpent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    BlockReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AllowNotifications = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customers_loyalty_levels_LoyaltyLevelId",
                        column: x => x.LoyaltyLevelId,
                        principalTable: "loyalty_levels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "customer_balance_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BalanceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    GamingSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_balance_transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_balance_transactions_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customer_packages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TotalMinutes = table.Column<int>(type: "integer", nullable: false),
                    RemainingMinutes = table.Column<int>(type: "integer", nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PurchasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_packages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_customer_packages_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_balance_transactions_CustomerId_CreatedAt",
                table: "customer_balance_transactions",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_balance_transactions_IdempotencyKey",
                table: "customer_balance_transactions",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_customer_packages_CustomerId",
                table: "customer_packages",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_customers_BranchId_Phone",
                table: "customers",
                columns: new[] { "BranchId", "Phone" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_LastName",
                table: "customers",
                column: "LastName");

            migrationBuilder.CreateIndex(
                name: "IX_customers_LoyaltyLevelId",
                table: "customers",
                column: "LoyaltyLevelId");

            migrationBuilder.CreateIndex(
                name: "IX_loyalty_levels_BranchId_Code",
                table: "loyalty_levels",
                columns: new[] { "BranchId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_balance_transactions");

            migrationBuilder.DropTable(
                name: "customer_packages");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "loyalty_levels");
        }
    }
}
