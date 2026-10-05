using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage4Cash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_registers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_registers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_registers_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "document_sequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    LastValue = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_sequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cash_shifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CashRegisterId = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpenedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClosedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OpeningCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ClosingCashActual = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ClosingCashExpected = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Discrepancy = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    DiscrepancyReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OpenComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CloseComment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SalesCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalesCard = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SalesOther = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundsCash = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashInTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CashOutTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpenseTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_shifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_shifts_cash_registers_CashRegisterId",
                        column: x => x.CashRegisterId,
                        principalTable: "cash_registers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CashShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cash_movements_cash_shifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "cash_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashShiftId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ComputerId = table.Column<Guid>(type: "uuid", nullable: true),
                    GamingSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipts_cash_shifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "cash_shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payments_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "receipt_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_receipt_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_receipt_items_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cash_movements_CashShiftId",
                table: "cash_movements",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_cash_registers_BranchId_Code",
                table: "cash_registers",
                columns: new[] { "BranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cash_shifts_CashRegisterId_Status",
                table: "cash_shifts",
                columns: new[] { "CashRegisterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_cash_shifts_Number",
                table: "cash_shifts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_sequences_BranchId_Type_Year",
                table: "document_sequences",
                columns: new[] { "BranchId", "Type", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_ReceiptId",
                table: "payments",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_receipt_items_ReceiptId",
                table: "receipt_items",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_CashShiftId",
                table: "receipts",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_IdempotencyKey",
                table: "receipts",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_receipts_Number",
                table: "receipts",
                column: "Number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_movements");

            migrationBuilder.DropTable(
                name: "document_sequences");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "receipt_items");

            migrationBuilder.DropTable(
                name: "receipts");

            migrationBuilder.DropTable(
                name: "cash_shifts");

            migrationBuilder.DropTable(
                name: "cash_registers");
        }
    }
}
