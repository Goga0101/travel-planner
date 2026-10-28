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
