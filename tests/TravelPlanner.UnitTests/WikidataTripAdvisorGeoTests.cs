using TravelPlanner.Infrastructure.Providers.Xotelo;

namespace TravelPlanner.UnitTests;

public class WikidataTripAdvisorGeoTests
{
    [Theory]
    [InlineData("294195", "g294195")]
    [InlineData("g294195", "g294195")]
    [InlineData(" 187497 ", "g187497")]
    [InlineData("d123-x", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Wikidata_TripAdvisor_Ids_Become_Location_Keys(string? id, string? expected) =>
        Assert.Equal(expected, WikidataTripAdvisorGeo.ParseId(id));
}
