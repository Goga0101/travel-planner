using TravelPlanner.Application.Features.Journeys;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.UnitTests;

public class JourneyTests
{
    private static readonly DateOnly Start = new(2026, 11, 10);
    private static Journey NewDraft() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "barcelona", "Barcelona", "ES", Start, Start.AddDays(2), new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));

    private static Journey NewJourney()
    {
        var j = NewDraft();
        j.ConfirmBooked(new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
        return j;
    }

    [Fact]
    public void A_Saved_Trip_Is_A_Draft_Whose_Days_Cannot_Be_Ticked_Until_It_Is_Booked()
    {
        var j = NewDraft();
        Assert.Equal(JourneyStatus.Draft, j.Status);
        Assert.Equal("journey.notBooked", j.CannotMark(Start, today: Start));

        j.ConfirmBooked(new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(JourneyStatus.Booked, j.Status);
        Assert.NotNull(j.BookedAtUtc);
        j.SetDay(Start, true, Start, DateTime.UtcNow);

        j.BackToDraft();
        Assert.Equal(JourneyStatus.Draft, j.Status);
        Assert.Null(j.BookedAtUtc);
        Assert.Empty(j.CompletedDays);
    }

    [Fact]
    public void The_Picked_Hotel_Is_Kept_Only_With_Its_Position()
    {
        var j = NewDraft();
        j.SetStay("  Hotel Arts ", 41.38, 2.19);
        Assert.Equal("Hotel Arts", j.StayName);
        j.SetStay("Hotel Arts", null, 2.19);
        Assert.Null(j.StayName);
        Assert.Null(j.StayLongitude);
    }

    [Theory]
    // Flight at 14:00: from 06:00 on the departure day until take-off.
    [InlineData("14:00", "2026-11-10 05:59", false)]
    [InlineData("14:00", "2026-11-10 06:00", true)]
    [InlineData("14:00", "2026-11-10 14:00", false)]
    // Early flight (05:30): the evening before, from 20:00.
    [InlineData("05:30", "2026-11-09 19:59", false)]
    [InlineData("05:30", "2026-11-09 20:00", true)]
    [InlineData("05:30", "2026-11-10 05:31", false)]
    // No flight time known: the whole departure day from 06:00.
    [InlineData(null, "2026-11-10 23:00", true)]
    [InlineData(null, "2026-11-11 00:00", false)]
    public void The_Good_Flight_Email_Is_Due_Before_Take_Off(string? departs, string now, bool due)
    {
        var j = NewJourney();
        if (departs is not null) j.DepartureLocal = Start.ToDateTime(TimeOnly.Parse(departs));
        Assert.Equal(due, TripReminderSchedule.FlightEmailDue(j, DateTime.Parse(now)));
    }

    [Theory]
    [InlineData("2026-11-11 07:59", false)]
    [InlineData("2026-11-11 08:00", true)]
    [InlineData("2026-11-11 23:59", true)]
    [InlineData("2026-11-12 08:00", false)]
    public void The_Trip_Started_Email_Is_Due_On_The_Second_Morning(string now, bool due) =>
        Assert.Equal(due, TripReminderSchedule.StartEmailDue(NewJourney(), DateTime.Parse(now)));

    [Fact]
    public void Reminders_Go_Only_For_Booked_Trips_And_Only_Once()
    {
        var draft = NewDraft();
        Assert.False(TripReminderSchedule.FlightEmailDue(draft, Start.ToDateTime(new TimeOnly(10, 0))));
        Assert.False(TripReminderSchedule.StartEmailDue(draft, Start.AddDays(1).ToDateTime(new TimeOnly(10, 0))));

        var j = NewJourney();
        j.MarkFlightEmailSent(DateTime.UtcNow);
        j.MarkStartEmailSent(DateTime.UtcNow);
        Assert.False(TripReminderSchedule.FlightEmailDue(j, Start.ToDateTime(new TimeOnly(10, 0))));
        Assert.False(TripReminderSchedule.StartEmailDue(j, Start.AddDays(1).ToDateTime(new TimeOnly(10, 0))));
    }

    [Fact]
    public void Reminder_Emails_Are_In_The_Trip_Language_And_Encode_What_They_Insert()
    {
        var user = new User("<b>Nino</b> Maisuradze", "nino@example.com", null, DateTime.UtcNow);
        var j = NewJourney();
        j.DepartureLocal = Start.ToDateTime(new TimeOnly(14, 5));

        var flight = TripEmails.FlightWishes(user, j, "https://example.org/site");
        Assert.Equal("nino@example.com", flight.ToAddress);
        Assert.Contains("მშვიდობიან ფრენას", flight.Subject);
        Assert.Contains("მადლობა", flight.Text);
        Assert.Contains("14:05", flight.Text);
        Assert.DoesNotContain("<b>Nino</b>", flight.Html);
        Assert.Contains($"https://example.org/site/ka/plans/{j.TripPlanId}", flight.Html);

        j.Lang = "en";
        var started = TripEmails.TripStarted(user, j, "https://example.org/site");
        Assert.Contains("has begun", started.Subject);
        Assert.Contains("https://example.org/site/en#leaderboard", started.Text);
    }

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
