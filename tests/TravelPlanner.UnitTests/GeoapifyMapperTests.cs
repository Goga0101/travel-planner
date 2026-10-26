using System.Text.Json;
using TravelPlanner.Infrastructure.Providers.Geoapify;

namespace TravelPlanner.UnitTests;

public class GeoapifyMapperTests
{
    private static JsonElement Feature(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Maps_Hotel_With_Osm_Tags()
    {
        var hotel = GeoapifyMapper.ToHotel(Feature("""
            {"type":"Feature","properties":{
              "name":"INNSiDE Barcelona Aeroport","lat":41.3125723,"lon":2.0684835,
              "address_line2":"Plaça del Pla de l'Estany 1-2, 08820 el Prat de Llobregat, Spain",
              "categories":["accommodation","accommodation.hotel","building"],
              "website":"https://www.melia.com/","contact":{"phone":"+34 933 78 10 00"},
              "name_international":{"en":"INNSiDE Barcelona Airport"},
              "datasource":{"raw":{"osm_type":"w","osm_id":313691453,"stars":"4"}}}}
            """))!;

        Assert.Equal("way/313691453", hotel.OsmId);
        Assert.Equal("INNSiDE Barcelona Aeroport", hotel.Name);
        Assert.Equal("INNSiDE Barcelona Airport", hotel.NameEn);
        Assert.Equal("hotel", hotel.Kind);
        Assert.Equal(4, hotel.Stars);
        Assert.Equal("+34 933 78 10 00", hotel.Phone);
        Assert.Equal("https://www.melia.com/", hotel.Website);
        Assert.StartsWith("Plaça del Pla", hotel.Address);
    }

    [Theory]
    [InlineData("""{"properties":{"lat":1,"lon":2,"categories":["accommodation.hotel"]}}""")]
    [InlineData("""{"properties":{"name":"No coordinates"}}""")]
    [InlineData("""{"type":"Feature"}""")]
    public void Skips_Hotels_Without_Name_Or_Coordinates(string json)
    {
        Assert.Null(GeoapifyMapper.ToHotel(Feature(json)));
    }

    [Fact]
    public void Ignores_Invalid_Star_Ratings()
    {
        var hotel = GeoapifyMapper.ToHotel(Feature("""
            {"properties":{"name":"H","lat":1,"lon":2,"categories":["accommodation.guest_house"],"datasource":{"raw":{"stars":"9"}}}}
            """))!;

        Assert.Null(hotel.Stars);
        Assert.Equal("guest_house", hotel.Kind);
    }

    [Fact]
    public void Maps_Stop_With_Its_Line_Numbers()
    {
        var stop = GeoapifyMapper.ToStop(Feature("""
            {"properties":{"name":"Aeroport T2B - Arribades","lat":41.30,"lon":2.07,
              "categories":["public_transport","public_transport.bus","public_transport.platform"],
              "datasource":{"raw":{"route_ref":"46;N18;N19;46"}}}}
            """))!;

        Assert.Equal("bus", stop.Mode);
        Assert.Equal(["46", "N18", "N19"], stop.Lines);
    }

    [Fact]
    public void Rail_Category_Wins_Over_Bus()
    {
        var stop = GeoapifyMapper.ToStop(Feature("""
            {"properties":{"name":"Aeroport T1","lat":41.29,"lon":2.07,"categories":["public_transport.bus","public_transport.subway"]}}
            """))!;

        Assert.Equal("subway", stop.Mode);
        Assert.Empty(stop.Lines);
    }
}
