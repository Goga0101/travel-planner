using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JourneyDraftsAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<DateTime>(
                name: "BookedAtUtc",
                table: "journeys",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<DateTime>(
                name: "DepartureLocal",
                table: "journeys",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FlightEmailSentAtUtc",
                table: "journeys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Lang",
                table: "journeys",
                type: "character varying(2)",
                maxLength: 2,
                nullable: false,
                defaultValue: "ka");

            migrationBuilder.AddColumn<DateTime>(
                name: "SavedAtUtc",
                table: "journeys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "StartEmailSentAtUtc",
                table: "journeys",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "journeys",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Booked");

            migrationBuilder.AddColumn<double>(
                name: "StayLatitude",
                table: "journeys",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StayLongitude",
                table: "journeys",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StayName",
                table: "journeys",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Trips saved before drafts existed were all marked as booked.
            migrationBuilder.Sql("UPDATE journeys SET \"SavedAtUtc\" = \"BookedAtUtc\" WHERE \"BookedAtUtc\" IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_journeys_Status_StartDate",
                table: "journeys",
                columns: new[] { "Status", "StartDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_journeys_Status_StartDate",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "DepartureLocal",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "FlightEmailSentAtUtc",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "Lang",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "SavedAtUtc",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "StartEmailSentAtUtc",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "StayLatitude",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "StayLongitude",
                table: "journeys");

            migrationBuilder.DropColumn(
                name: "StayName",
                table: "journeys");

            migrationBuilder.AlterColumn<DateTime>(
                name: "BookedAtUtc",
                table: "journeys",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);
        }
    }
}
