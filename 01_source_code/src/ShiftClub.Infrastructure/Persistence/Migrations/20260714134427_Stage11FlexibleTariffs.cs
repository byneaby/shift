using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftClub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage11FlexibleTariffs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowPause",
                table: "tariffs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "AvailableFrom",
                table: "tariffs",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "AvailableTo",
                table: "tariffs",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ColorHex",
                table: "tariffs",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DaysOfWeekMask",
                table: "tariffs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "tariffs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxDurationMinutes",
                table: "tariffs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinDurationMinutes",
                table: "tariffs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PausedAt",
                table: "gaming_sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RemainingSecondsAtPause",
                table: "gaming_sessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalPausedSeconds",
                table: "gaming_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TimeBankMinutes",
                table: "customers",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowPause",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "AvailableFrom",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "AvailableTo",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "ColorHex",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "DaysOfWeekMask",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "MaxDurationMinutes",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "MinDurationMinutes",
                table: "tariffs");

            migrationBuilder.DropColumn(
                name: "PausedAt",
                table: "gaming_sessions");

            migrationBuilder.DropColumn(
                name: "RemainingSecondsAtPause",
                table: "gaming_sessions");

            migrationBuilder.DropColumn(
                name: "TotalPausedSeconds",
                table: "gaming_sessions");

            migrationBuilder.DropColumn(
                name: "TimeBankMinutes",
                table: "customers");
        }
    }
}
