using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260807090000_Stage32ShiftCase")]
public partial class Stage32ShiftCase : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CaseKeysBalance",
            table: "customers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.CreateTable(
            name: "case_definitions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                EconomicsNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ExpectedCostKzt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                LimitsJson = table.Column<string>(type: "text", nullable: true),
                ShowProbabilitiesToUsers = table.Column<bool>(type: "boolean", nullable: false),
                IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                KeyCost = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_case_definitions", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_case_definitions_Code",
            table: "case_definitions",
            column: "Code",
            unique: true);

        migrationBuilder.CreateTable(
            name: "case_prizes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CaseDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                PrizeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                PrizeType = table.Column<int>(type: "integer", nullable: false),
                Rarity = table.Column<int>(type: "integer", nullable: false),
                Weight = table.Column<int>(type: "integer", nullable: false),
                CostEstimateKzt = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false),
                ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                DailyLimit = table.Column<int>(type: "integer", nullable: true),
                TotalLimit = table.Column<int>(type: "integer", nullable: true),
                RequiresClaim = table.Column<bool>(type: "boolean", nullable: false),
                SortOrder = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_case_prizes", x => x.Id);
                table.ForeignKey(
                    name: "FK_case_prizes_case_definitions_CaseDefinitionId",
                    column: x => x.CaseDefinitionId,
                    principalTable: "case_definitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_case_prizes_CaseDefinitionId_PrizeCode",
            table: "case_prizes",
            columns: new[] { "CaseDefinitionId", "PrizeCode" },
            unique: true);

        migrationBuilder.CreateTable(
            name: "case_key_ledger",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                Delta = table.Column<int>(type: "integer", nullable: false),
                BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                Reason = table.Column<int>(type: "integer", nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                EmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_case_key_ledger", x => x.Id);
                table.ForeignKey(
                    name: "FK_case_key_ledger_customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_case_key_ledger_CustomerId_CreatedAt",
            table: "case_key_ledger",
            columns: new[] { "CustomerId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_case_key_ledger_IdempotencyKey",
            table: "case_key_ledger",
            column: "IdempotencyKey",
            unique: true,
            filter: "\"IdempotencyKey\" IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "case_openings",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                CaseDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                CasePrizeId = table.Column<Guid>(type: "uuid", nullable: false),
                PrizeCodeSnapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PrizeNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                RaritySnapshot = table.Column<int>(type: "integer", nullable: false),
                PrizeTypeSnapshot = table.Column<int>(type: "integer", nullable: false),
                PayloadJsonSnapshot = table.Column<string>(type: "text", nullable: false),
                ImageUrlSnapshot = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                KeyCost = table.Column<int>(type: "integer", nullable: false),
                WeightRoll = table.Column<int>(type: "integer", nullable: false),
                WeightTotal = table.Column<int>(type: "integer", nullable: false),
                ClubDayKey = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_case_openings", x => x.Id);
                table.ForeignKey(
                    name: "FK_case_openings_case_definitions_CaseDefinitionId",
                    column: x => x.CaseDefinitionId,
                    principalTable: "case_definitions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_case_openings_case_prizes_CasePrizeId",
                    column: x => x.CasePrizeId,
                    principalTable: "case_prizes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_case_openings_customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_case_openings_CaseDefinitionId_ClubDayKey_CasePrizeId",
            table: "case_openings",
            columns: new[] { "CaseDefinitionId", "ClubDayKey", "CasePrizeId" });

        migrationBuilder.CreateIndex(
            name: "IX_case_openings_CasePrizeId",
            table: "case_openings",
            column: "CasePrizeId");

        migrationBuilder.CreateIndex(
            name: "IX_case_openings_CustomerId_CreatedAt",
            table: "case_openings",
            columns: new[] { "CustomerId", "CreatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_case_openings_IdempotencyKey",
            table: "case_openings",
            column: "IdempotencyKey",
            unique: true,
            filter: "\"IdempotencyKey\" IS NOT NULL");

        migrationBuilder.CreateTable(
            name: "case_user_rewards",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                CaseOpeningId = table.Column<Guid>(type: "uuid", nullable: false),
                CasePrizeId = table.Column<Guid>(type: "uuid", nullable: false),
                PrizeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                PrizeType = table.Column<int>(type: "integer", nullable: false),
                PayloadJson = table.Column<string>(type: "text", nullable: false),
                ImageUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Status = table.Column<int>(type: "integer", nullable: false),
                AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClaimedByEmployeeId = table.Column<Guid>(type: "uuid", nullable: true),
                ClaimNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_case_user_rewards", x => x.Id);
                table.ForeignKey(
                    name: "FK_case_user_rewards_case_openings_CaseOpeningId",
                    column: x => x.CaseOpeningId,
                    principalTable: "case_openings",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_case_user_rewards_customers_CustomerId",
                    column: x => x.CustomerId,
                    principalTable: "customers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_case_user_rewards_CaseOpeningId",
            table: "case_user_rewards",
            column: "CaseOpeningId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_case_user_rewards_CustomerId_Status",
            table: "case_user_rewards",
            columns: new[] { "CustomerId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_case_user_rewards_Status_CreatedAt",
            table: "case_user_rewards",
            columns: new[] { "Status", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "case_user_rewards");
        migrationBuilder.DropTable(name: "case_key_ledger");
        migrationBuilder.DropTable(name: "case_openings");
        migrationBuilder.DropTable(name: "case_prizes");
        migrationBuilder.DropTable(name: "case_definitions");
        migrationBuilder.DropColumn(name: "CaseKeysBalance", table: "customers");
    }
}
