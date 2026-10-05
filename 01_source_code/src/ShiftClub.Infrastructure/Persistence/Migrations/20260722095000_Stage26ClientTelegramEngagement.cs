using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260722095000_Stage26ClientTelegramEngagement")]
public partial class Stage26ClientTelegramEngagement : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "TelegramUserId",
            table: "customers",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "TelegramLinkedAt",
            table: "customers",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "VisitStreakDays",
            table: "customers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateOnly>(
            name: "VisitStreakLastDate",
            table: "customers",
            type: "date",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "BirthdayGiftYear",
            table: "customers",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "PendingBarRewards",
            table: "customers",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<bool>(
            name: "ComfortHideBalance",
            table: "customers",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "ComfortSoundEnabled",
            table: "customers",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "ComfortLanguage",
            table: "customers",
            type: "character varying(8)",
            maxLength: 8,
            nullable: false,
            defaultValue: "ru");

        migrationBuilder.AddColumn<int>(
            name: "ComfortBrightness",
            table: "customers",
            type: "integer",
            nullable: false,
            defaultValue: 100);

        migrationBuilder.CreateIndex(
            name: "IX_customers_TelegramUserId",
            table: "customers",
            column: "TelegramUserId");

        migrationBuilder.CreateTable(
            name: "telegram_auth_tickets",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BranchId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ComputerId = table.Column<Guid>(type: "uuid", nullable: true),
                SessionId = table.Column<Guid>(type: "uuid", nullable: true),
                CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                TelegramUserId = table.Column<long>(type: "bigint", nullable: true),
                Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ResultMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_telegram_auth_tickets", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_telegram_auth_tickets_Code",
            table: "telegram_auth_tickets",
            column: "Code",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_telegram_auth_tickets_Status_ExpiresAt",
            table: "telegram_auth_tickets",
            columns: new[] { "Status", "ExpiresAt" });

        migrationBuilder.CreateIndex(
            name: "IX_telegram_auth_tickets_ComputerId",
            table: "telegram_auth_tickets",
            column: "ComputerId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "telegram_auth_tickets");
        migrationBuilder.DropIndex(name: "IX_customers_TelegramUserId", table: "customers");
        migrationBuilder.DropColumn(name: "TelegramUserId", table: "customers");
        migrationBuilder.DropColumn(name: "TelegramLinkedAt", table: "customers");
        migrationBuilder.DropColumn(name: "VisitStreakDays", table: "customers");
        migrationBuilder.DropColumn(name: "VisitStreakLastDate", table: "customers");
        migrationBuilder.DropColumn(name: "BirthdayGiftYear", table: "customers");
        migrationBuilder.DropColumn(name: "PendingBarRewards", table: "customers");
        migrationBuilder.DropColumn(name: "ComfortHideBalance", table: "customers");
        migrationBuilder.DropColumn(name: "ComfortSoundEnabled", table: "customers");
        migrationBuilder.DropColumn(name: "ComfortLanguage", table: "customers");
        migrationBuilder.DropColumn(name: "ComfortBrightness", table: "customers");
    }
}
