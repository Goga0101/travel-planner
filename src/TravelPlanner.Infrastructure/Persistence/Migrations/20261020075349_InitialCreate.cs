using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "destinations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Country = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Region = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Popularity = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    BookingComCityId = table.Column<long>(type: "bigint", nullable: true),
                    TaxiBaseFare = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TaxiPerKm = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    TaxiTariffCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    TaxiTariffSource = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_destinations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "trip_searches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginAirportIata = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    EarliestDeparture = table.Column<DateOnly>(type: "date", nullable: false),
                    LatestReturn = table.Column<DateOnly>(type: "date", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    Travelers = table.Column<int>(type: "integer", nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BudgetCurrency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    DestinationMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DataMode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProgressPercent = table.Column<int>(type: "integer", nullable: false),
                    ProgressMessage = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Warnings = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    preferences = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_searches", x => x.Id);
                    table.CheckConstraint("ck_trip_searches_budget", "\"BudgetAmount\" > 0");
                    table.CheckConstraint("ck_trip_searches_duration", "\"DurationDays\" BETWEEN 2 AND 30");
                    table.CheckConstraint("ck_trip_searches_travelers", "\"Travelers\" BETWEEN 1 AND 9");
                    table.CheckConstraint("ck_trip_searches_window", "\"LatestReturn\" > \"EarliestDeparture\"");
                });

            migrationBuilder.CreateTable(
                name: "airports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IataCode = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    IsDepartureOption = table.Column<bool>(type: "boolean", nullable: false),
                    OfficialWebsiteUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_airports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_airports_destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "destination_transport_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    VerifiedOn = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_destination_transport_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_destination_transport_links_destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripSearchId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<decimal>(type: "numeric(7,2)", precision: 7, scale: 2, nullable: false),
                    ScoreBreakdownJson = table.Column<string>(type: "jsonb", nullable: false),
                    DepartureDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Travelers = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    BudgetAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    TotalEstimatedCost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    RemainingBudget = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    BudgetFit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MissingCostCategories = table.Column<List<string>>(type: "text[]", nullable: false),
                    Warnings = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PricesRefreshedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_plans", x => x.Id);
                    table.CheckConstraint("ck_trip_plans_dates", "\"ReturnDate\" > \"DepartureDate\"");
                    table.ForeignKey(
                        name: "FK_trip_plans_destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trip_plans_trip_searches_TripSearchId",
                        column: x => x.TripSearchId,
                        principalTable: "trip_searches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_search_destinations",
                columns: table => new
                {
                    TripSearchId = table.Column<Guid>(type: "uuid", nullable: false),
                    DestinationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_search_destinations", x => new { x.TripSearchId, x.DestinationId });
                    table.ForeignKey(
                        name: "FK_trip_search_destinations_destinations_DestinationId",
                        column: x => x.DestinationId,
                        principalTable: "destinations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_trip_search_destinations_trip_searches_TripSearchId",
                        column: x => x.TripSearchId,
                        principalTable: "trip_searches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_plan_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProviderName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ProviderOfferId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    ConvertedPrice = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    ExchangeRate = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    ExchangeRateSource = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExchangeRateAsOfUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IncludedInTotal = table.Column<bool>(type: "boolean", nullable: false),
                    BookingUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    RetrievedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Verification = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trip_plan_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trip_plan_items_trip_plans_TripPlanId",
                        column: x => x.TripPlanId,
                        principalTable: "trip_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_airports_DestinationId",
                table: "airports",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_airports_IataCode",
                table: "airports",
                column: "IataCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_airports_IsDepartureOption",
                table: "airports",
                column: "IsDepartureOption");

            migrationBuilder.CreateIndex(
                name: "IX_destination_transport_links_DestinationId_Url",
                table: "destination_transport_links",
                columns: new[] { "DestinationId", "Url" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_destinations_Code",
                table: "destinations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_destinations_IsActive_Popularity",
                table: "destinations",
                columns: new[] { "IsActive", "Popularity" });

            migrationBuilder.CreateIndex(
                name: "IX_trip_plan_items_ExpiresAtUtc",
                table: "trip_plan_items",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_trip_plan_items_TripPlanId_SortOrder",
                table: "trip_plan_items",
                columns: new[] { "TripPlanId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_trip_plans_DestinationId",
                table: "trip_plans",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_trip_plans_TripSearchId_Rank",
                table: "trip_plans",
                columns: new[] { "TripSearchId", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trip_search_destinations_DestinationId",
                table: "trip_search_destinations",
                column: "DestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_trip_searches_CreatedAtUtc",
                table: "trip_searches",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_trip_searches_Status",
                table: "trip_searches",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "airports");

            migrationBuilder.DropTable(
                name: "destination_transport_links");

            migrationBuilder.DropTable(
                name: "trip_plan_items");

            migrationBuilder.DropTable(
                name: "trip_search_destinations");

            migrationBuilder.DropTable(
                name: "trip_plans");

            migrationBuilder.DropTable(
                name: "destinations");

            migrationBuilder.DropTable(
                name: "trip_searches");
        }
    }
}
