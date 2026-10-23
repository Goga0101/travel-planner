using TravelPlanner.Application.Planning;
using TravelPlanner.Domain.ValueObjects;

namespace TravelPlanner.UnitTests;

public class DateCombinationTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Fact]
    public void FiveDayTrip_Means_FiveCalendarDays_And_FourNights()
    {
        var dates = TravelDates.ForDuration(new DateOnly(2026, 11, 20), 5);

        Assert.Equal(new DateOnly(2026, 11, 24), dates.Return);
        Assert.Equal(5, dates.DurationDays);
        Assert.Equal(4, dates.Nights);
        Assert.Equal(dates.Departure, dates.CheckIn);
        Assert.Equal(dates.Return, dates.CheckOut);
        Assert.Equal(4, TravelDates.NightsForDuration(5));
    }

    [Fact]
    public void Generates_All_Combinations_Within_Window()
    {
        var combos = DateCombinationGenerator.Generate(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 30), 5, Today, maxCombinations: 20);

        Assert.Equal(7, combos.Count); // departures 20..26, returns 24..30
        Assert.Equal(new TravelDates(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24)), combos[0]);
        Assert.Equal(new TravelDates(new DateOnly(2026, 11, 26), new DateOnly(2026, 11, 30)), combos[^1]);
        Assert.All(combos, c =>
        {
            Assert.Equal(4, c.Nights);
            Assert.True(c.Return <= new DateOnly(2026, 11, 30));
        });
    }

    [Fact]
    public void Window_Exactly_Equal_To_Duration_Gives_Single_Combination()
    {
        var combos = DateCombinationGenerator.Generate(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), 5, Today, 10);

        Assert.Single(combos);
    }

    [Fact]
    public void Window_Shorter_Than_Duration_Gives_No_Combinations()
    {
        Assert.Empty(DateCombinationGenerator.Generate(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 23), 5, Today, 10));
    }

    [Fact]
    public void Departures_On_Or_Before_Today_Are_Skipped()
    {
        var combos = DateCombinationGenerator.Generate(new DateOnly(2026, 10, 18), new DateOnly(2026, 10, 30), 5, Today, 20);

        Assert.Equal(new DateOnly(2026, 10, 21), combos[0].Departure);
        Assert.All(combos, c => Assert.True(c.Departure > Today));
    }

    [Fact]
    public void Sampling_Keeps_First_And_Last_And_Respects_Limit()
    {
        var combos = DateCombinationGenerator.Generate(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 5, Today, maxCombinations: 4);

        Assert.Equal(4, combos.Count);
        Assert.Equal(new DateOnly(2026, 11, 1), combos[0].Departure);
        Assert.Equal(new DateOnly(2026, 11, 26), combos[^1].Departure);
        Assert.Equal(combos.Count, combos.Distinct().Count());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public void Too_Short_Duration_Gives_No_Combinations(int duration)
    {
        Assert.Empty(DateCombinationGenerator.Generate(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 30), duration, Today, 10));
    }
}
