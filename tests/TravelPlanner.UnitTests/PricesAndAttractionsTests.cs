using System.Text.Json;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Infrastructure.Providers.Geoapify;
using TravelPlanner.Infrastructure.Providers.Xotelo;

namespace TravelPlanner.UnitTests;

public class PricesAndAttractionsTests
{
    [Fact]
    public void Xotelo_List_Maps_Hotels_With_Rating_Range_And_Location()
    {
        var hotels = XoteloMapper.ParseList(JsonDocument.Parse("""
            {"error":null,"result":{"list":[
              {"name":"Hotel Astoria","key":"g187497-d228590","review_summary":{"rating":4.3,"count":3819},
               "price_ranges":{"maximum":315,"minimum":133},"geo":{"latitude":41.39,"longitude":2.15}},
              {"name":"No key"}]}}
            """).RootElement);

        var h = Assert.Single(hotels);
        Assert.Equal("g187497-d228590", h.Key);
        Assert.Equal(4.3, h.Rating);
        Assert.Equal(3819, h.ReviewCount);
        Assert.Equal(133m, h.TypicalNightlyMin);
        Assert.Equal(41.39, h.Latitude);
    }

    [Fact]
    public void Xotelo_Rates_Are_Sorted_And_Named()
    {
        var rates = XoteloMapper.ParseRates(JsonDocument.Parse("""
            {"result":{"currency":"EUR","rates":[
              {"code":"BookingCom","name":"Booking.com","rate":125},{"code":"Agoda","name":"Agoda.com","rate":107},
              {"code":"CtripTA","name":"Trip","rate":125.4},{"code":"Broken","name":"x","rate":0}]}}
            """).RootElement, "EUR");

        Assert.Equal(["Agoda", "BookingCom", "CtripTA"], rates.Select(r => r.Code));
        Assert.Equal("Booking.com", rates[1].Name);
        Assert.Equal("Trip.com", rates[2].Name);
    }

    [Fact]
    public void Xotelo_Rates_In_Another_Currency_Are_Ignored()
    {
        var rates = XoteloMapper.ParseRates(JsonDocument.Parse("""
            {"result":{"currency":"USD","rates":[{"code":"Agoda","name":"Agoda.com","rate":107}]}}
            """).RootElement, "EUR");

        Assert.Empty(rates);
    }

    [Theory]
    [InlineData(new[] { 70.0, 125.0, 77.0 }, 77.0)]
    [InlineData(new[] { 100.0, 80.0, 120.0, 90.0 }, 95.0)]
    public void Median_Of_Nightly_Prices(double[] values, double expected)
    {
        Assert.Equal((decimal)expected, GetHotelPricesHandler.Median(values.Select(v => (decimal)v)));
    }

    [Fact]
    public void Summary_Keeps_Abbreviations_Inside_The_First_Sentence()
    {
        var text = "პართენონი — ძვ. წ. V საუკუნის ტაძარი ათენის აკროპოლისზე, ქალღმერთ ათენასადმი მიძღვნილი. " +
                   "ის ანტიკური საბერძნეთის ხელოვნების სიმბოლოა. მესამე წინადადება აღარ უნდა გამოჩნდეს.";

        var summary = AttractionSource.Shorten(text)!;

        Assert.StartsWith("პართენონი — ძვ. წ. V საუკუნის ტაძარი", summary);
        Assert.EndsWith("სიმბოლოა.", summary);
    }

    [Theory]
    [InlineData("entertainment.museum,tourism.sights", "museum")]
    [InlineData("tourism.sights.castle", "castle")]
    [InlineData("tourism.sights.place_of_worship.church", "worship")]
    [InlineData("leisure.park", "park")]
    [InlineData("tourism.sights.archaeological_site", "heritage")]
    [InlineData("tourism.attraction", "sight")]
    public void Sight_Kind_From_Categories(string categories, string kind)
    {
        Assert.Equal(kind, AttractionSource.Kind(categories.Split(',')));
    }
}
