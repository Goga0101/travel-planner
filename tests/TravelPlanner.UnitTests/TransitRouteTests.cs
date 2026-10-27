using System.Text.Json;
using TravelPlanner.Infrastructure.Providers.Transitous;

namespace TravelPlanner.UnitTests;

public class TransitRouteTests
{
    private static readonly TimeSpan Cet = TimeSpan.FromHours(1);

    private const string Body = """
    { "itineraries": [
      { "legs": [
        { "mode": "WALK", "startTime": "2026-10-28T08:00:00Z", "endTime": "2026-10-28T08:13:00Z", "from": { "name": "START" }, "to": { "name": "Aeroport T2B" } },
        { "mode": "BUS", "routeShortName": "A2", "headsign": "Barcelona", "agencyName": "Aerobús", "startTime": "2026-10-28T08:15:00Z", "endTime": "2026-10-28T08:50:00Z",
          "from": { "name": "Aeroport T2B" }, "to": { "name": "Pl. Catalunya" }, "intermediateStops": [ {}, {}, {} ] },
        { "mode": "WALK", "startTime": "2026-10-28T08:50:00Z", "endTime": "2026-10-28T08:52:00Z", "from": { "name": "Pl. Catalunya" }, "to": { "name": "END" } }
      ] },
      { "legs": [
        { "mode": "BUS", "routeShortName": "A2", "startTime": "2026-10-28T08:30:00Z", "endTime": "2026-10-28T09:25:00Z", "from": { "name": "Aeroport T2B" }, "to": { "name": "Pl. Catalunya" } }
      ] },
      { "legs": [
        { "mode": "LONG_DISTANCE", "routeShortName": "IC50", "startTime": "2026-10-28T08:00:00Z", "endTime": "2026-10-28T09:00:00Z", "from": { "name": "A" }, "to": { "name": "B" } }
      ] },
      { "legs": [
        { "mode": "COACH", "routeShortName": "X", "startTime": "2026-10-28T08:00:00Z", "endTime": "2026-10-29T08:00:00Z", "from": { "name": "A" }, "to": { "name": "B" } }
      ] }
    ] }
    """;

    [Fact]
    public void Journeys_Are_Mapped_To_Local_Times_And_Implausible_Ones_Dropped()
    {
        var itineraries = TransitousRouter.Map(JsonDocument.Parse(Body).RootElement, Cet, 150);

        // The second A2 trip is the same option (slower) and long-distance/day-long journeys are dropped.
        var it = Assert.Single(itineraries);
        Assert.Equal(52, it.DurationMinutes);
        Assert.Equal(0, it.Transfers);
        Assert.Equal(new DateTime(2026, 10, 28, 9, 0, 0), it.DepartureLocal);
        var bus = it.Legs.Single(l => l.Mode == "BUS");
        Assert.Equal("A2", bus.Line);
        Assert.Equal(4, bus.Stops);
        Assert.Equal("", it.Legs[0].From); // START is not a place name
    }

    [Fact]
    public void Trips_Beyond_The_Timetable_Use_The_Next_Same_Weekday()
    {
        var now = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero); // Tuesday
        var wished = new DateTime(2026, 12, 5, 7, 10, 0);                      // Saturday

        var used = TransitousRouter.TimetableDate(wished, now, Cet);

        Assert.Equal(new DateTime(2026, 10, 31, 7, 10, 0), used);
        Assert.Equal(new DateTime(2026, 10, 29, 9, 0, 0), TransitousRouter.TimetableDate(new DateTime(2026, 10, 29, 9, 0, 0), now, Cet));
    }
}
