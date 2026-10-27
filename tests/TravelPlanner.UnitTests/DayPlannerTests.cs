using System.Text.Json;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Infrastructure.Providers.Ticketmaster;

namespace TravelPlanner.UnitTests;

public class DayPlannerTests
{
    private const double Lat = 41.3851, Lon = 2.1734;

    private static ActivityPlace Place(string id, string interest, double dLat, double dLon, bool notable = false) =>
        new(id, id, interest, interest, Lat + dLat, Lon + dLon, null, null, null, null, null, null, notable);

    private static readonly IReadOnlyList<ActivityPlace> Places =
    [
        Place("sight-near", Interests.Culture, 0.001, 0),
        Place("sight-famous", Interests.Culture, 0.03, 0, notable: true),
        Place("sight-2", Interests.Culture, 0.002, 0.002, notable: true),
        Place("food-by-famous", Interests.Food, 0.031, 0),
        Place("food-centre", Interests.Food, 0, 0.001),
        Place("food-3", Interests.Food, 0.01, 0.01),
        Place("food-4", Interests.Food, 0.02, 0.02),
        Place("bar-1", Interests.Bars, 0.031, 0.001),
        Place("bar-2", Interests.Bars, 0.0, 0.002),
    ];

    private static DayPlanRequest Request(DateTime? arrival = null, DateTime? departure = null, TripPace pace = TripPace.Normal,
        params string[] interests) =>
        new(new DateOnly(2026, 12, 5), new DateOnly(2026, 12, 8), arrival, departure,
            interests.Length > 0 ? interests : [Interests.Culture, Interests.Food, Interests.Bars], pace, Lat, Lon);

    [Fact]
    public void Day_Starts_With_The_Most_Notable_Sight_And_Continues_Nearby()
    {
        var days = DayPlanner.Build(Request(), Places, []);

        var first = days[0].Items;
        Assert.Equal("sight-famous", first.Single(i => i.Slot == "morning").Place!.Place.Id);
        // Lunch is the restaurant next to the morning sight, not the one in the centre.
        Assert.Equal("food-by-famous", first.Single(i => i.Slot == "lunch").Place!.Place.Id);
        Assert.Contains(first, i => i.Slot == "night" && i.Place!.Place.Interest == Interests.Bars);
    }

    [Fact]
    public void Places_Are_Not_Repeated_And_Alternatives_Are_Offered()
    {
        var days = DayPlanner.Build(Request(), Places, []);

        var ids = days.SelectMany(d => d.Items).Where(i => i.Place is not null).Select(i => i.Place!.Place.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var lunch = days[0].Items.Single(i => i.Slot == "lunch");
        Assert.NotEmpty(lunch.Alternatives);
        Assert.DoesNotContain(lunch.Alternatives, a => a.Place.Id == lunch.Place!.Place.Id);
    }

    [Fact]
    public void Arrival_And_Departure_Days_Fit_Around_The_Flights()
    {
        var days = DayPlanner.Build(Request(arrival: new DateTime(2026, 12, 5, 16, 30, 0), departure: new DateTime(2026, 12, 8, 14, 0, 0)), Places, []);

        Assert.Equal(4, days.Count);
        Assert.Equal(PlanDayKind.Arrival, days[0].Kind);
        // Landed 16:30: only slots from 18:30 on (dinner, night).
        Assert.All(days[0].Items, i => Assert.True(i.Time >= new TimeOnly(18, 30)));
        Assert.NotEmpty(days[0].Items);
        // Take-off 14:00: done by 10:30, so only the 10:00 slot would end too late → nothing but an early start.
        Assert.All(days[3].Items, i => Assert.True(i.Time.ToTimeSpan() + TimeSpan.FromHours(1.5) <= new TimeSpan(10, 30, 0)));
        Assert.Equal(PlanDayKind.Full, days[1].Kind);
    }

    [Fact]
    public void Overnight_Arrival_Leaves_The_First_Day_Empty()
    {
        var days = DayPlanner.Build(Request(arrival: new DateTime(2026, 12, 6, 4, 0, 0)), Places, []);
        Assert.Empty(days[0].Items);
    }

    [Fact]
    public void A_Matching_Event_Is_Scheduled_On_Its_Day()
    {
        var concert = new CityEvent("e1", "Concert", new DateOnly(2026, 12, 6), new TimeOnly(21, 0), "Palau", Lat, Lon, "Music", "Rock",
            null, 30m, 80m, "EUR", "https://www.ticketmaster.es/event/1");

        var days = DayPlanner.Build(Request(), Places, [concert]);

        var item = Assert.Single(days[1].Items, i => i.Event is not null);
        Assert.Equal(new TimeOnly(21, 0), item.Time);
        Assert.DoesNotContain(days[1].Items, i => i.Slot == "night"); // the 22:00 bar overlaps the concert
    }

    [Fact]
    public void Relaxed_Pace_Has_Fewer_Slots_Than_Intense()
    {
        var relaxed = DayPlanner.Build(Request(pace: TripPace.Relaxed), Places, []);
        var intense = DayPlanner.Build(Request(pace: TripPace.Intense), Places, []);
        Assert.True(relaxed[0].Items.Count < intense[0].Items.Count);
    }

    [Fact]
    public void Ticketmaster_Events_Are_Mapped_With_Prices_And_Images()
    {
        const string json = """
        { "_embedded": { "events": [
          { "id": "Z1", "name": "Rock Night", "url": "https://www.ticketmaster.es/event/Z1",
            "dates": { "start": { "localDate": "2026-12-06", "localTime": "21:00:00" } },
            "classifications": [ { "segment": { "name": "Music" }, "genre": { "name": "Rock" } } ],
            "priceRanges": [ { "currency": "EUR", "min": 35.0, "max": 90.0 } ],
            "images": [ { "url": "https://s1.ticketm.net/a.jpg", "ratio": "3_2", "width": 1024 },
                        { "url": "https://s1.ticketm.net/b.jpg", "ratio": "16_9", "width": 640 } ],
            "_embedded": { "venues": [ { "name": "Razzmatazz", "location": { "latitude": "41.397", "longitude": "2.191" } } ] } },
          { "id": "Z2", "name": "No date" }
        ] } }
        """;
        var e = Assert.Single(TicketmasterEventSource.Map(JsonDocument.Parse(json).RootElement));
        Assert.Equal(new DateOnly(2026, 12, 6), e.Date);
        Assert.Equal(new TimeOnly(21, 0), e.Time);
        Assert.Equal("Music", e.Segment);
        Assert.Equal(35m, e.PriceMin);
        Assert.Equal("https://s1.ticketm.net/b.jpg", e.ImageUrl);
        Assert.Equal("Razzmatazz", e.Venue);
        Assert.Equal(41.397, e.Latitude);
    }
}
