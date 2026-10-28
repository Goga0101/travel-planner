using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDestinationGatewayAirports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "GatewayAirports",
                table: "destinations",
                type: "text[]",
                nullable: false,
                // Existing destinations have their own airports.
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GatewayAirports",
                table: "destinations");
        }
    }
}
