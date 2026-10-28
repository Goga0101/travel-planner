using System.Text.Json;
using TravelPlanner.Application.Features.Transit;
using TravelPlanner.Infrastructure.Providers.Transitous;

namespace TravelPlanner.UnitTests;

public class LiveTransitTests
{
    [Fact]
    public void Map_Trips_Become_Timed_Segments_With_Their_Shape()
    {
        var body = JsonDocument.Parse("""
            [{"trips":[{"tripId":"t1","routeShortName":"337"}],"routeColor":"00b38b","mode":"BUS",
              "from":{"name":"A"},"to":{"name":"B"},"departure":"2026-10-28T08:03:00Z","arrival":"2026-10-28T08:04:00Z",
              "realTime":true,"polyline":"_p~iF~ps|U"},
             {"trips":[],"mode":"BUS","departure":"2026-10-28T08:03:00Z","arrival":"2026-10-28T08:04:00Z","polyline":"x"}]
            """).RootElement;

        var segment = Assert.Single(TransitousLive.MapSegments(body));
        Assert.Equal("t1", segment.TripId);
        Assert.Equal("337", segment.Line);
        Assert.Equal("B", segment.ToStop);
        Assert.True(segment.RealTime);
        Assert.Equal(TimeSpan.FromMinutes(1), segment.Arrival - segment.Departure);
    }

    [Fact]
    public void Photon_Address_Is_Named_By_Street_And_Number()
    {
        var feature = JsonDocument.Parse("""
            {"properties":{"osm_key":"building","type":"house","housenumber":"37","street":"ილია ჭავჭავაძის გამზირი",
              "district":"ვაკის რაიონი","city":"თბილისი","country":"საქართველო","countrycode":"ge"},
             "geometry":{"type":"Point","coordinates":[44.7607,41.7108]}}
            """).RootElement;

        var place = TransitousLive.PhotonPlace(feature)!;
        Assert.Equal("ილია ჭავჭავაძის გამზირი 37", place.Name);
        Assert.Equal("ვაკის რაიონი, თბილისი, საქართველო", place.Area);
        Assert.Equal("GE", place.CountryCode);
        Assert.Equal("address", place.Kind);
        Assert.Equal(41.7108, place.Latitude);
    }

    [Fact]
    public void Taxi_Apps_Follow_The_Country_And_Fall_Back_To_Global_Ones()
    {
        Assert.Equal(["Bolt", "Yandex Go", "Maxim"], RideHailingApps.For("GE").Select(a => a.Name));
        Assert.Contains("Lyft", RideHailingApps.For("us").Select(a => a.Name));
        Assert.Equal(["Bolt", "Uber"], RideHailingApps.For("AQ").Select(a => a.Name));
        Assert.Equal(["Bolt", "Uber"], RideHailingApps.For(null).Select(a => a.Name));
    }

    [Fact]
    public void Trips_From_Tbilisi_To_The_Regions_Point_To_The_Right_Station()
    {
        var sagarejo = IntercityStations.For(41.6934, 44.8015, 41.734, 45.330, "en");
        Assert.Equal("Samgori (Navtlughi) bus station", sagarejo[0].Name);
        Assert.True(sagarejo[0].Recommended);

        var ozurgeti = IntercityStations.For(41.6934, 44.8015, 41.924, 42.006, "ka");
        Assert.Equal("დიდუბის ავტოსადგური", ozurgeti[0].Name);

        Assert.Empty(IntercityStations.For(41.6934, 44.8015, 41.7260, 44.7650, "en")); // a trip inside the city
        Assert.Empty(IntercityStations.For(42.269, 42.705, 41.643, 41.636, "en"));     // starts outside Tbilisi
    }

    [Fact]
    public void Without_A_Timetable_A_Line_Calling_Near_Both_Ends_Becomes_An_Estimated_Ride()
    {
        // Monastiraki → Omonia area (Athens): line 035 calls near both ends, 227 only near the start.
        var near = new List<Application.Providers.NearbyTransitStop>
        {
            new("ΜΟΝΑΣΤΗΡΑΚΙ", "bus", ["035", "227"], 37.9765, 23.7255),
            new("ΘΗΣΕΙΟ", "bus", ["227"], 37.9770, 23.7200),
        };
        var far = new List<Application.Providers.NearbyTransitStop> { new("ΟΜΟΝΟΙΑ", "bus", ["035", "049"], 37.9840, 23.7280) };

        var options = StopLineRoutes.Build(near, far, 37.9760, 23.7250, "Start", 37.9845, 23.7285, "End", new DateTime(2026, 10, 30, 12, 0, 0));

        var it = Assert.Single(options);
        Assert.True(it.Estimated);
        var ride = it.Legs[1];
        Assert.Equal(("BUS", "035", "ΜΟΝΑΣΤΗΡΑΚΙ", "ΟΜΟΝΟΙΑ"), (ride.Mode, ride.Line, ride.From, ride.To));
        Assert.Equal(37.9765, ride.Path!.FromLatitude);
        Assert.Equal("WALK", it.Legs[0].Mode);
        Assert.Equal("12:00", it.DepartureTime);
    }

    [Fact]
    public void Tbilisi_Metro_Rides_Towards_The_Centre_With_A_Change_At_Station_Square()
    {
        // Anna Politkovskaya St 16 (near State University) → Samgori bus station.
        var it = CityMetros.Route(41.72048, 44.71045, "Start", 41.6860, 44.8519, "Samgori", new DateTime(2026, 10, 28, 14, 0, 0), "en")!;

        var rides = it.Legs.Where(l => l.Mode == "SUBWAY").ToList();
        Assert.Equal(["2", "1"], rides.Select(r => r.Line));
        Assert.Equal(("State University", "Station Square"), (rides[0].From, rides[0].To));
        Assert.Equal(("Station Square", "Samgori"), (rides[1].From, rides[1].To));
        Assert.Equal(6, rides[0].Stops);
        Assert.Equal("Station Square", rides[0].Headsign); // towards the line's end
        Assert.True(it.Estimated);
        Assert.Equal(1, it.Transfers);
        Assert.Equal(it.Legs.Sum(l => l.DurationMinutes) + 4 + 3, it.DurationMinutes); // plus the waits
    }

    [Fact]
    public void No_Metro_Option_Far_From_Stations_Or_Outside_The_City()
    {
        Assert.Null(CityMetros.Route(41.75, 44.70, "Dighomi edge", 41.80, 44.70, "x", DateTime.Today, "en"));
        Assert.Null(CityMetros.Route(47.4979, 19.0402, "Budapest", 47.50, 19.06, "x", DateTime.Today, "en"));
        Assert.Null(CityMetros.Route(41.7221, 44.7977, "Station Square", 41.7225, 44.7958, "next door", DateTime.Today, "en"));
    }

    [Fact]
    public void Station_Shapes_Encode_As_Google_Polylines()
    {
        // Google's documented example points.
        Assert.Equal("_p~iF~ps|U_ulLnnqC_mqNvxq`@",
            CityMetros.Encode([new("a", "a", 38.5, -120.2), new("b", "b", 40.7, -120.95), new("c", "c", 43.252, -126.453)]));
    }

    private sealed class AnyStops : Application.Providers.ITransitStopSource
    {
        public bool IsConfigured => true;
        public Task<IReadOnlyList<Application.Providers.NearbyTransitStop>> FindAsync(double latitude, double longitude, int radiusMeters, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Application.Providers.NearbyTransitStop>>([new($"Metro {latitude:F2}", "subway", [], latitude, longitude)]);
    }

    [Fact]
    public async Task Stations_In_Different_Cities_Are_Not_One_Ride()
    {
        // Tbilisi and Berlin both have metro stations; that is no journey.
        Assert.Empty(await StopLineRoutes.FindAsync(new AnyStops(), 41.6934, 44.8015, "Tbilisi", 52.52, 13.405, "Berlin", DateTime.Today, CancellationToken.None));
        Assert.NotEmpty(await StopLineRoutes.FindAsync(new AnyStops(), 41.6934, 44.8015, "Liberty Sq", 41.7225, 44.7958, "Station Sq", DateTime.Today, CancellationToken.None));
    }

    [Fact]
    public void Short_Hops_Get_No_Estimated_Ride()
    {
        var stop = new Application.Providers.NearbyTransitStop("A", "bus", ["1"], 37.97, 23.72);
        Assert.Empty(StopLineRoutes.Build([stop], [stop with { Latitude = 37.9705 }], 37.97, 23.72, "a", 37.9705, 23.72, "b", DateTime.Today));
    }

    [Fact]
    public void Mtskheta_Is_A_Town_Served_From_Didube_Not_Part_Of_Tbilisi()
    {
        var mtskheta = IntercityStations.TownAt(41.8424, 44.7194);
        Assert.Equal("Mtskheta", mtskheta!.Value.Town.NameEn);
        Assert.Equal("Didube bus station", mtskheta.Value.Station.NameEn);
        Assert.False(IntercityStations.InTbilisi(41.8424, 44.7194));
        Assert.True(IntercityStations.InTbilisi(41.72048, 44.71045));       // Vake
        Assert.False(IntercityStations.InTbilisi(42.269, 42.705));          // Kutaisi

        // Mtskheta is close (~15 km) but a different town: Tbilisi → Mtskheta gets the station guide.
        Assert.Equal("Didube bus station", IntercityStations.For(41.72048, 44.71045, 41.8424, 44.7194, "en")[0].Name);
    }

    [Fact]
    public void A_Minibus_Leg_Is_Timed_By_Road_Distance()
    {
        var leg = IntercityStations.MinibusLeg("Mtskheta", 41.8424, 44.7194, "Didube", 41.7518, 44.7791, new DateTime(2026, 10, 28, 15, 0, 0), "en");
        Assert.Equal("MINIBUS", leg.Mode);
        Assert.InRange(leg.DurationMinutes, 15, 25); // ~11 km in a straight line
        Assert.Equal("15:00", leg.DepartureTime);
        Assert.Equal(41.7518, leg.Path!.ToLatitude);
    }

    [Fact]
    public void Overpass_Lists_Taxis_With_A_Phone_First()
    {
        var body = JsonDocument.Parse("""
            {"elements":[
              {"type":"node","lat":41.71,"lon":44.78,"tags":{"amenity":"taxi"}},
              {"type":"way","center":{"lat":41.80,"lon":44.90},"tags":{"office":"taxi","name":"City Taxi","phone":"+995 32 200 00 00","website":"http://x.ge"}}
            ]}
            """).RootElement;

        var list = OverpassTaxiSource.Map(body, 41.71, 44.78);
        Assert.Equal("City Taxi", list[0].Name);
        Assert.True(list[0].IsCompany);
        Assert.Null(list[0].Website); // plain HTTP links are dropped
        Assert.Null(list[1].Phone);
    }
}
