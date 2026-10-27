using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorldPlacesAndFoundFlights : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "Continents",
                table: "trip_searches",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "FoundFlightsJson",
                table: "trip_searches",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<bool>(
                name: "IsCatalog",
                table: "destinations",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "CityCode",
                table: "airports",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Continent",
                table: "airports",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "airports",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_airports_CityCode",
                table: "airports",
                column: "CityCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_airports_CityCode",
                table: "airports");

            migrationBuilder.DropColumn(
                name: "Continents",
                table: "trip_searches");

            migrationBuilder.DropColumn(
                name: "FoundFlightsJson",
                table: "trip_searches");

            migrationBuilder.DropColumn(
                name: "IsCatalog",
                table: "destinations");

            migrationBuilder.DropColumn(
                name: "CityCode",
                table: "airports");

            migrationBuilder.DropColumn(
                name: "Continent",
                table: "airports");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "airports");
        }
    }
}
