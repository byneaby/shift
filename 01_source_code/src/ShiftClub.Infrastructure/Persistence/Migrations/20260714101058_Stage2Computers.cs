using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage2Computers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "computers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                    ZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WindowsName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MacAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MapX = table.Column<double>(type: "double precision", nullable: true),
                    MapY = table.Column<double>(type: "double precision", nullable: true),
                    ClientVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    WindowsVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CpuName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GpuName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    RamMb = table.Column<int>(type: "integer", nullable: true),
                    ScreenResolution = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeatAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsApproved = table.Column<bool>(type: "boolean", nullable: false),
                    RegistrationCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    RegistrationCodeExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeviceTokenHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsMaintenance = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Tags = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastDiagnosticsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_computers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_computers_branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_computers_zones_ZoneId",
                        column: x => x.ZoneId,
                        principalTable: "zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "computer_commands",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComputerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InitiatedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResultJson = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_computer_commands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_computer_commands_computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "computer_heartbeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ComputerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CpuLoadPercent = table.Column<double>(type: "double precision", nullable: true),
                    RamUsedPercent = table.Column<double>(type: "double precision", nullable: true),
                    FreeDiskMb = table.Column<long>(type: "bigint", nullable: true),
                    UptimeSeconds = table.Column<long>(type: "bigint", nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ClientVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    StatusNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ShellRunning = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_computer_heartbeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_computer_heartbeats_computers_ComputerId",
                        column: x => x.ComputerId,
                        principalTable: "computers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_computer_commands_ComputerId_Status",
                table: "computer_commands",
                columns: new[] { "ComputerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_computer_commands_IdempotencyKey",
                table: "computer_commands",
                column: "IdempotencyKey");

            migrationBuilder.CreateIndex(
                name: "IX_computer_heartbeats_ComputerId_ReceivedAt",
                table: "computer_heartbeats",
                columns: new[] { "ComputerId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_computers_BranchId",
                table: "computers",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_computers_InstallationId",
                table: "computers",
                column: "InstallationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_computers_RegistrationCode",
                table: "computers",
                column: "RegistrationCode");

            migrationBuilder.CreateIndex(
                name: "IX_computers_Status",
                table: "computers",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_computers_ZoneId",
                table: "computers",
                column: "ZoneId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "computer_commands");

            migrationBuilder.DropTable(
                name: "computer_heartbeats");

            migrationBuilder.DropTable(
                name: "computers");
        }
    }
}
