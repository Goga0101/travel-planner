using TravelPlanner.Application.Providers;
using TravelPlanner.Infrastructure.Providers.Xotelo;

namespace TravelPlanner.UnitTests;

public class XoteloBudgetTests
{
    private static HotelPriceOffer Hotel(string key, double rating, decimal? min) =>
        new(key, key, rating, 100, null, null, min, min * 2, []);

    [Fact]
    public void Budget_Candidates_Keep_Hotels_Whose_Range_Starts_Near_The_Budget_Best_Rated_First()
    {
        var hotels = new[] { Hotel("cheap", 4.0, 30), Hotel("lux", 4.9, 300), Hotel("edge", 4.5, 64), Hotel("unknown", 3.0, null), Hotel("cheap", 4.0, 30) };

        var picked = XoteloMapper.BudgetCandidates(hotels, 50, 10);

        Assert.Equal(["edge", "cheap", "unknown"], picked.Select(h => h.Key));
        Assert.Single(XoteloMapper.BudgetCandidates(hotels, 50, 1));
    }
}
