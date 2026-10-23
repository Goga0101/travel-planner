using TravelPlanner.Domain.ValueObjects;

namespace TravelPlanner.Application.Planning;

public static class DateCombinationGenerator
{
    /// <summary>
    /// All departure/return pairs of exactly <paramref name="durationDays"/> calendar days that fit inside
    /// [earliestDeparture, latestReturn]. Departures must be after <paramref name="today"/>.
    /// When there are more than <paramref name="maxCombinations"/>, an evenly spaced sample is returned
    /// (always including the first and last possible departure).
    /// </summary>
    public static IReadOnlyList<TravelDates> Generate(
        DateOnly earliestDeparture, DateOnly latestReturn, int durationDays, DateOnly today, int maxCombinations)
    {
        if (durationDays < TravelDates.MinimumDurationDays || maxCombinations < 1) return [];

        var firstDeparture = earliestDeparture > today ? earliestDeparture : today.AddDays(1);
        var lastDeparture = latestReturn.AddDays(-(durationDays - 1));
        if (lastDeparture < firstDeparture) return [];

        var all = new List<TravelDates>();
        for (var d = firstDeparture; d <= lastDeparture; d = d.AddDays(1))
            all.Add(TravelDates.ForDuration(d, durationDays));

        return Sample(all, maxCombinations);
    }

    /// <summary>Evenly spaced sample that keeps the first and last element.</summary>
    public static IReadOnlyList<T> Sample<T>(IReadOnlyList<T> items, int max)
    {
        if (items.Count <= max) return items;
        if (max == 1) return [items[0]];

        var result = new List<T>(max);
        var seen = new HashSet<int>();
        for (var i = 0; i < max; i++)
        {
            var index = (int)Math.Round(i * (items.Count - 1) / (double)(max - 1), MidpointRounding.AwayFromZero);
            if (seen.Add(index)) result.Add(items[index]);
        }
        return result;
    }
}
