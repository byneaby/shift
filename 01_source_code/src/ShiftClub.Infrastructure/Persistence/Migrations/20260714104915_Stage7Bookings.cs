using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage7Bookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ContactName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    PrepaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalEstimated = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PrepayMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    PrepayReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    GraceMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CancelledByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ArrivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NoShowAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bookings_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_bookings_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_bookings_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "booking_computers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BookingId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComputerId = table.Column<Guid>(type: "uuid", nullable: false),
                    GamingSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_computers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_booking_computers_bookings_BookingId",
                        column: x => x.BookingId,
                        principalTable: "bookings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_booking_computers_computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_booking_computers_BookingId_ComputerId",
                table: "booking_computers",
                columns: new[] { "BookingId", "ComputerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_computers_ComputerId",
                table: "booking_computers",
                column: "ComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_BranchId_StartsAt_EndsAt",
                table: "bookings",
                columns: new[] { "BranchId", "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_bookings_CustomerId",
                table: "bookings",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_IdempotencyKey",
                table: "bookings",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_Number",
                table: "bookings",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookings_Status",
                table: "bookings",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_bookings_ZoneId",
                table: "bookings",
                column: "ZoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_computers");

            migrationBuilder.DropTable(
                name: "bookings");
        }
    }
}
