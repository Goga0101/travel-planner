using System.Text.Json;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;
using TravelPlanner.Infrastructure.Providers.Tripadvisor;

namespace TravelPlanner.UnitTests;

public class HotelReviewTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Matches_The_Picked_Hotel_By_Name_Ignoring_Filler_Words_And_Accents()
    {
        var candidates = new List<TerraLocation>
        {
            new(1, "Hotel Arts Barcelona", 0.05),
            new(2, "Hotel Catalonia Barcelona Plaza", 0.12),
        };

        Assert.Equal(2, TerraMapper.BestMatch("Catalonia Barcelona Plaza", candidates)?.Id);
        Assert.Equal(1, TerraMapper.BestMatch("Arts Barcelona", candidates)?.Id);
        Assert.Equal(7, TerraMapper.BestMatch("Hôtel Mercure Paris", [new TerraLocation(7, "Mercure Paris Centre", 0.2)])?.Id);
    }

    [Fact]
    public void Does_Not_Match_A_Different_Hotel_Nearby()
    {
        var candidates = new List<TerraLocation> { new(1, "Hotel Arts Barcelona", 0.2), new(2, "W Barcelona", 0.25) };
        Assert.Null(TerraMapper.BestMatch("Casa Fuster", candidates));
    }

    [Fact]
    public void Parses_Nearby_Locations()
    {
        var list = TerraMapper.ParseNearby(Json("""
            {"data":[{"distance_kilometers":0.08,"location":{"id":190567,
              "names":[{"language":"es","primary":false,"value":"Hotel Arts"},{"language":"en","primary":true,"value":"Hotel Arts Barcelona"}]}}],
             "pagination":{"page":1,"size":3}}
            """));

        var l = Assert.Single(list);
        Assert.Equal(190567, l.Id);
        Assert.Equal("Hotel Arts Barcelona", l.Name);
        Assert.Equal(0.08, l.DistanceKm);
    }

    [Fact]
    public void Parses_Reviews_And_Keeps_Only_Tripadvisor_Links()
    {
        var reviews = TerraMapper.ParseReviews(Json("""
            {"data":[
              {"id":1023,"publish_ts":"2026-10-28T09:15:00Z","rating":5,
               "title":[{"language":"en","primary":true,"value":"Great views"}],
               "text":[{"language":"en","primary":true,"value":"Lovely stay by the beach."}],
               "travel_date":"2026-10","trip_type":"COUPLES",
               "url":"https://www.tripadvisor.com/ShowUserReviews-g187497-d190567-r1023-Hotel_Arts_Barcelona-Barcelona_Catalonia.html",
               "user":{"username":"Nino T","geo":"Tbilisi, Georgia","avatar_url":{"url":"https://media.tacdn.com/a.jpg"}},
               "rating_icon_url":{"url":"https://www.tripadvisor.com/img/5.svg"},
               "owner_response":{"text":[{"language":"en","primary":true,"value":"Thank you!"}]}},
              {"id":1024,"publish_ts":"2026-10-20T09:15:00Z","rating":3,
               "text":[{"language":"en","primary":true,"value":"OK."}],
               "url":"https://evil.example.com/x","user":{"username":"x","avatar_url":{"url":"http://insecure/a.jpg"}}},
              {"id":1025,"publish_ts":"not a date","rating":4,"text":[{"language":"en","value":"skipped"}]}
            ]}
            """));

        Assert.Equal(2, reviews.Count);
        var r = reviews[0];
        Assert.Equal("1023", r.Id);
        Assert.Equal(5, r.Rating);
        Assert.Equal("Great views", r.Title);
        Assert.Equal("Lovely stay by the beach.", r.Text);
        Assert.Equal("COUPLES", r.TripType);
        Assert.Equal("Nino T", r.Author);
        Assert.Equal("Tbilisi, Georgia", r.AuthorLocation);
        Assert.Equal("https://media.tacdn.com/a.jpg", r.AvatarUrl);
        Assert.Equal("Thank you!", r.OwnerResponse);
        Assert.Null(reviews[1].Url);
        Assert.Null(reviews[1].AvatarUrl);
        Assert.Equal("https://www.tripadvisor.com/Hotel_Review-g187497-d190567-Reviews-Hotel_Arts_Barcelona-Barcelona_Catalonia.html",
            TerraMapper.HotelPageFromReview(r.Url));
    }

    [Fact]
    public void Shows_The_Last_Two_Weeks_Newest_First()
    {
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var dto = GetHotelReviewsHandler.Shape(Result(now.AddDays(-20), now.AddDays(-2), now.AddDays(-10), now.AddDays(-15)), now);

        Assert.True(dto.Recent);
        Assert.Equal(14, dto.WindowDays);
        Assert.Equal([now.AddDays(-2), now.AddDays(-10)], dto.Reviews.Select(r => r.PublishedAt));
    }

    [Fact]
    public void Falls_Back_To_The_Latest_Few_When_Nobody_Reviewed_Recently()
    {
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        var dto = GetHotelReviewsHandler.Shape(Result(now.AddDays(-40), now.AddDays(-20), now.AddDays(-30), now.AddDays(-60), now.AddDays(-90)), now);

        Assert.False(dto.Recent);
        Assert.Equal([now.AddDays(-20), now.AddDays(-30), now.AddDays(-40)], dto.Reviews.Select(r => r.PublishedAt));
        Assert.Equal(4.0, dto.AverageRating);
    }

    private static HotelReviewResult Result(params DateTimeOffset[] dates) =>
        new("Tripadvisor", true, "Hotel", null, dates.Select((d, i) =>
            new HotelReview($"{i}", 4, null, "text", "en", d, null, null, null, null, null, null, null, null)).ToList());
}
