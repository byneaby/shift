using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ShiftClub.Infrastructure.Persistence;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShiftClubDbContext))]
[Migration("20260724043000_Stage29QrAuthHardening")]
public partial class Stage29QrAuthHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "telegram_auth_tickets",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(24)",
            oldMaxLength: 24);

        migrationBuilder.AddColumn<string>(
            name: "PendingDisplayName",
            table: "telegram_auth_tickets",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ClientNonce",
            table: "telegram_auth_tickets",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "AuthRedeemed",
            table: "telegram_auth_tickets",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_telegram_auth_tickets_Purpose_ClientNonce_Status",
            table: "telegram_auth_tickets",
            columns: new[] { "Purpose", "ClientNonce", "Status" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_telegram_auth_tickets_Purpose_ClientNonce_Status",
            table: "telegram_auth_tickets");

        migrationBuilder.DropColumn(name: "PendingDisplayName", table: "telegram_auth_tickets");
        migrationBuilder.DropColumn(name: "ClientNonce", table: "telegram_auth_tickets");
        migrationBuilder.DropColumn(name: "AuthRedeemed", table: "telegram_auth_tickets");

        migrationBuilder.AlterColumn<string>(
            name: "Status",
            table: "telegram_auth_tickets",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32);
    }
}
