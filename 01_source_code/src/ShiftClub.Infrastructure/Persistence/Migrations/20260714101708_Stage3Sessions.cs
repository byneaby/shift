using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage3Sessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentSessionId",
                table: "computers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tariffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BillingMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PricePerHour = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    MinCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    FixedDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                    FixedPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tariffs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_tariffs_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tariffs_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "gaming_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ComputerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: false),
                    TariffId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuestName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PaymentMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlannedEndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BasePrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DebtAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StartedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CancelReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Warning15Sent = table.Column<bool>(type: "boolean", nullable: false),
                    Warning10Sent = table.Column<bool>(type: "boolean", nullable: false),
                    Warning5Sent = table.Column<bool>(type: "boolean", nullable: false),
                    Warning1Sent = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gaming_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_gaming_sessions_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gaming_sessions_computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gaming_sessions_tariffs_TariffId",
                        column: x => x.TariffId,
                        principalTable: "tariffs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_gaming_sessions_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    DetailsJson = table.Column<string>(type: "text", nullable: true),
                    PriceBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PriceAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PlannedEndsAtBefore = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PlannedEndsAtAfter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_session_history_gaming_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "gaming_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_computers_CurrentSessionId",
                table: "computers",
                column: "CurrentSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_gaming_sessions_BranchId",
                table: "gaming_sessions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_gaming_sessions_ComputerId",
                table: "gaming_sessions",
                column: "ComputerId");

            migrationBuilder.CreateIndex(
                name: "IX_gaming_sessions_ComputerId_Status",
                table: "gaming_sessions",
                columns: new[] { "ComputerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_gaming_sessions_IdempotencyKey",
                table: "gaming_sessions",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_gaming_sessions_Status",
                table: "gaming_sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_gaming_sessions_TariffId",
                table: "gaming_sessions",
                column: "TariffId");

            migrationBuilder.CreateIndex(
                name: "IX_gaming_sessions_ZoneId",
                table: "gaming_sessions",
                column: "ZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_session_history_SessionId_CreatedAt",
                table: "session_history",
                columns: new[] { "SessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_BranchId_Code",
                table: "tariffs",
                columns: new[] { "BranchId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_tariffs_ZoneId",
                table: "tariffs",
                column: "ZoneId");

            migrationBuilder.AddForeignKey(
                name: "FK_computers_gaming_sessions_CurrentSessionId",
                table: "computers",
                column: "CurrentSessionId",
                principalTable: "gaming_sessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_computers_gaming_sessions_CurrentSessionId",
                table: "computers");

            migrationBuilder.DropTable(
                name: "session_history");

            migrationBuilder.DropTable(
                name: "gaming_sessions");

            migrationBuilder.DropTable(
                name: "tariffs");

            migrationBuilder.DropIndex(
                name: "IX_computers_CurrentSessionId",
                table: "computers");

            migrationBuilder.DropColumn(
                name: "CurrentSessionId",
                table: "computers");
        }
    }
}
