using TravelPlanner.Application.Features.TripPlans;
using static TravelPlanner.UnitTests.TestData;

namespace TravelPlanner.UnitTests;

public class NearbyLinksTests
{
    [Fact]
    public void Distance_Between_Barcelona_Airport_And_Centre_Is_About_13_Km()
    {
        var meters = NearbyLinks.DistanceMeters(41.2974, 2.0833, 41.3874, 2.1686);

        Assert.InRange(meters, 12_000, 13_000);
    }

    [Fact]
    public void Transit_Directions_Use_Invariant_Coordinates()
    {
        var url = NearbyLinks.TransitDirections(41.2974, 2.0833, 41.3125723, 2.0684835);

        Assert.Equal("https://www.google.com/maps/dir/?api=1&origin=41.2974,2.0833&destination=41.312572,2.068484&travelmode=transit", url);
        Assert.NotNull(LinkPolicy("google.com").Sanitize(url));
    }

    [Fact]
    public void Uber_Link_Prefills_Pickup_And_Dropoff()
    {
        var url = NearbyLinks.Uber(41.2974, 2.0833, "BCN", 41.3125723, 2.0684835, "Hotel & Spa");

        Assert.StartsWith("https://m.uber.com/ul/?action=setPickup&pickup[latitude]=41.2974&pickup[longitude]=2.0833&pickup[nickname]=BCN", url);
        Assert.EndsWith("&dropoff[nickname]=Hotel%20%26%20Spa", url);
        Assert.NotNull(LinkPolicy("uber.com").Sanitize(url));
    }

    [Theory]
    [InlineData("https://www.hotelmidmost.com/", "https://www.hotelmidmost.com/")]
    [InlineData("http://insecure.example.com/", null)]
    [InlineData("https://user:pass@example.com/", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("not a url", null)]
    [InlineData(null, null)]
    public void Curated_Websites_Must_Be_Plain_Https(string? input, string? expected)
    {
        Assert.Equal(expected, NearbyLinks.CuratedHttps(input));
    }

    [Fact]
    public void TripAdvisor_Pages_Open_On_The_Trip_Dates()
    {
        var url = NearbyLinks.TripAdvisorWithStay("https://www.tripadvisor.com/Hotel_Review-g186338-d1-Reviews-X.html",
            new DateOnly(2026, 11, 21), new DateOnly(2026, 11, 25), 2, 1);
        Assert.Equal("https://www.tripadvisor.com/Hotel_Review-g186338-d1-Reviews-X.html?staydates=2026_11_21_2026_11_25&reqNum=1&rooms=1&adults=2", url);
        Assert.Equal("https://example.com/hotel", NearbyLinks.TripAdvisorWithStay("https://example.com/hotel", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), 1, 1));
        Assert.Null(NearbyLinks.TripAdvisorWithStay("http://www.tripadvisor.com/x", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), 1, 1));
    }
}
