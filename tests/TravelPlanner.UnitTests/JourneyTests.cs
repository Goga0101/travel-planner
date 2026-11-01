using TravelPlanner.Application.Features.Journeys;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.UnitTests;

public class JourneyTests
{
    private static readonly DateOnly Start = new(2026, 11, 10);
    private static Journey NewJourney() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "barcelona", "Barcelona", "ES", Start, Start.AddDays(2), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Completes_When_Every_Day_Is_Ticked_And_Reopens_When_One_Is_Unticked()
    {
        var j = NewJourney();
        var today = Start.AddDays(5);
        var now = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(3, j.DayCount);

        j.SetDay(Start, true, today, now);
        j.SetDay(Start.AddDays(2), true, today, now);
        Assert.False(j.IsCompleted);

        j.SetDay(Start.AddDays(1), true, today, now);
        Assert.True(j.IsCompleted);
        Assert.Equal([Start, Start.AddDays(1), Start.AddDays(2)], j.CompletedDays);

        j.SetDay(Start.AddDays(1), false, today, now);
        Assert.False(j.IsCompleted);
        Assert.Equal(2, j.CompletedDays.Count);
    }

    [Fact]
    public void Ticking_The_Same_Day_Twice_Counts_Once()
    {
        var j = NewJourney();
        j.SetDay(Start, true, Start, DateTime.UtcNow);
        j.SetDay(Start, true, Start, DateTime.UtcNow);
        Assert.Single(j.CompletedDays);
    }

    [Fact]
    public void Days_That_Have_Not_Begun_Or_Are_Outside_The_Trip_Cannot_Be_Ticked()
    {
        var j = NewJourney();
        Assert.Equal("journey.dayInFuture", j.CannotMark(Start.AddDays(1), today: Start));
        Assert.Equal("journey.dayOutsideTrip", j.CannotMark(Start.AddDays(-1), today: Start.AddDays(10)));
        Assert.Equal("journey.dayOutsideTrip", j.CannotMark(Start.AddDays(3), today: Start.AddDays(10)));
        Assert.Null(j.CannotMark(Start, today: Start));
        Assert.Throws<InvalidOperationException>(() => j.SetDay(Start.AddDays(1), true, Start, DateTime.UtcNow));
    }

    [Fact]
    public void Public_Name_Shows_First_Name_And_Last_Initial()
    {
        Assert.Equal("Nino M.", JourneyMapping.PublicName("Nino Maisuradze"));
        Assert.Equal("გიორგი ბ.", JourneyMapping.PublicName("გიორგი ბერიძე"));
        Assert.Equal("Madonna", JourneyMapping.PublicName("  Madonna "));
        Assert.Equal("Ana K.", JourneyMapping.PublicName("Ana Maria Kapanadze"));
    }

    [Fact]
    public void Leaderboard_Ranks_By_Trips_Then_Days_Then_Countries_Then_Who_Got_There_First()
    {
        var early = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var late = early.AddDays(5);
        var rows = new[]
        {
            new GetLeaderboardHandler.Row("A", 2, 10, ["IT"], [], late, false, false),
            new GetLeaderboardHandler.Row("B", 3, 8, ["IT"], [], late, true, false),
            new GetLeaderboardHandler.Row("C", 2, 10, ["IT", "FR"], [], late, false, true),
            new GetLeaderboardHandler.Row("D", 2, 10, ["IT", "FR"], [], early, false, false),
            new GetLeaderboardHandler.Row("E", 2, 12, ["ES"], [], late, false, false),
        };

        var ranked = GetLeaderboardHandler.Rank(rows).ToList();

        Assert.Equal(["B", "E", "D", "C", "A"], ranked.Select(r => r.Name));
        Assert.Equal([1, 2, 3, 4, 5], ranked.Select(r => r.Rank));
        Assert.True(ranked[0].IsSample);
        Assert.True(ranked[3].IsYou);
    }
}
