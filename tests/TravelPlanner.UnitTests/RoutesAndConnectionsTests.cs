using System.Text.Json;
using TravelPlanner.Application.Providers;
using TravelPlanner.Infrastructure.Catalog;
using TravelPlanner.Infrastructure.Providers.Travelpayouts;
using static TravelPlanner.UnitTests.TestData;

namespace TravelPlanner.UnitTests;

public class RoutesAndConnectionsTests
{
    private const string LongHaulLink =
        "/search/TBS2912SYD19011?t=CA17985846001798790700003015TBSURCTFUPEKSYD18003546001800547800003640SYDPEKISTSAWTBS_6f86e908e86458b89cc69440c8454e42_99913&search_date=21102026";

    [Fact]
    public void Aviasales_Link_Reveals_The_Connection_Airports_Of_Both_Directions()
    {
        var route = AviasalesRoute.Parse(LongHaulLink);

        Assert.Equal(["TBS", "URC", "TFU", "PEK", "SYD"], route.Legs[0]);
        Assert.Equal(["SYD", "PEK", "IST", "SAW", "TBS"], route.Legs[1]);
        Assert.Equal(["URC", "TFU", "PEK"], route.Via(0, "TBS", "SYD", 3));
        // IST → SAW is a change of airport inside Istanbul: three codes for two transfers is accepted.
        Assert.Equal(["PEK", "IST", "SAW"], route.Via(1, "SYD", "TBS", 2));
    }

    [Theory]
    [InlineData("/search/TBS2011VIE24111?t=abc")]
    [InlineData("/search/x?nott=CA17985846001798790700003015TBSSYD")]
    [InlineData(null)]
    public void Unexpected_Links_Yield_No_Route(string? link)
    {
        var route = AviasalesRoute.Parse(link);
        Assert.Empty(route.Legs);
        Assert.Null(route.Via(0, "TBS", "SYD", 1));
    }

    [Fact]
    public void Route_That_Does_Not_Match_The_Ticket_Is_Ignored()
    {
        Assert.Null(AviasalesRoute.Parse(LongHaulLink).Via(0, "TBS", "MEL", 3));
        Assert.Null(AviasalesRoute.Parse(LongHaulLink).Via(0, "TBS", "SYD", 4));
    }

    [Fact]
    public void Travelpayouts_Ticket_Gets_Arrival_Times_And_Connections()
    {
        var json = $$"""
        { "success": true, "currency": "eur", "data": [
          { "origin": "TBS", "destination": "SYD", "origin_airport": "TBS", "destination_airport": "SYD", "price": 1083, "airline": "CA",
            "flight_number": 782, "departure_at": "2026-12-29T22:50:00+04:00", "return_at": "2027-01-19T10:30:00+11:00",
            "transfers": 3, "return_transfers": 2, "duration_to": 1330, "duration_back": 1480, "link": "{{LongHaulLink}}" }
        ]}
        """;
        var response = JsonSerializer.Deserialize<TravelpayoutsPricesResponse>(json)!;
        var offer = Assert.Single(new TravelpayoutsFlightOfferMapper(new TravelpayoutsOptions()).MapAll(response, 1, "EUR", Now));

        // 22:50 +04 (18:50 UTC) plus 22h10 = 17:00 UTC on 30 Dec = 04:00 on 31 Dec in Sydney (+11).
        Assert.Equal(new DateTime(2026, 12, 31, 4, 0, 0), offer.Outbound.ArrivalLocal);
        Assert.Equal(["URC", "TFU", "PEK"], offer.Outbound.ViaAirports);
        Assert.Equal(["PEK", "IST", "SAW"], offer.Inbound.ViaAirports);
        Assert.Empty(offer.Tickets);
    }

    private static OneWayFare Fare(string from, string to, string fromCity, string toCity, string departure, int minutes, decimal price) =>
        new(from, to, fromCity, toCity, DateTimeOffset.Parse(departure), DateTimeOffset.Parse(departure).AddMinutes(minutes), 0, "W6", price,
            null, [], $"{from}{to}{departure}");

    [Fact]
    public void Self_Transfer_Needs_Enough_Time_To_Change_And_Picks_The_Cheapest()
    {
        var day = new DateOnly(2026, 12, 5);
        var direct = new[] { Fare("TBS", "BCN", "TBS", "BCN", "2026-12-05T08:00:00+04:00", 360, 250) };
        var first = new[] { Fare("KUT", "BGY", "KUT", "MIL", "2026-12-05T06:00:00+04:00", 240, 40) };
        var second = new[]
        {
            Fare("BGY", "BCN", "MIL", "BCN", "2026-12-05T08:00:00+01:00", 100, 20),  // leaves 1 h after landing: too tight
            Fare("BGY", "BCN", "MIL", "BCN", "2026-12-05T13:00:00+01:00", 100, 35),
            Fare("MXP", "BCN", "MIL", "BCN", "2026-12-05T11:00:00+01:00", 100, 15),  // other airport: needs 5 h, has 4 h
            Fare("MXP", "BCN", "MIL", "BCN", "2026-12-05T13:30:00+01:00", 100, 30),
        };

        var options = TravelpayoutsFlightProvider.Options(day, direct, [(first, second)]);

        // Lands in Bergamo at 07:00 local: 13:30 from Malpensa (30) beats 13:00 from Bergamo (35); the direct ticket is dearer.
        Assert.Equal(2, options.Count);
        Assert.Equal(["KUT-BGY", "MXP-BCN"], options[0].Select(f => $"{f.From}-{f.To}"));
        Assert.Equal("TBS-BCN", $"{options[1][0].From}-{options[1][0].To}");
    }

    [Fact]
    public void Self_Transfer_Combination_Is_Flagged_Per_Direction()
    {
        FlightTicket Ticket(bool back) => new(back, "A", "B", default, null, 0, null, 10, "EUR", null);
        Assert.False(FlightTicket.IsSelfTransfer([Ticket(false), Ticket(true)]));
        Assert.True(FlightTicket.IsSelfTransfer([Ticket(false), Ticket(false), Ticket(true)]));
    }

    [Fact]
    public void World_Cities_Are_Found_By_Name_And_Code()
    {
        var reader = new WorldAirportsFileReader(
            Microsoft.Extensions.Options.Options.Create(new CatalogOptions { WorldAirportsPath = WorldAirportsPath() }),
            new TestHostEnvironment());
        var places = new WorldPlaces(reader);

        Assert.Equal("DTM", places.SearchCities("Dortmund", 5)[0].CityCode);
        Assert.Equal("Oceania", places.FindCity("SYD")!.Continent);
        Assert.Contains("LHR", places.FindCity("LHR")!.AirportCodes);
        Assert.Equal("LON", places.FindCity("LGW")!.CityCode);
    }

    private static string WorldAirportsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TravelPlanner.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "TravelPlanner.API", "catalog", "world-airports.json");
    }

    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
