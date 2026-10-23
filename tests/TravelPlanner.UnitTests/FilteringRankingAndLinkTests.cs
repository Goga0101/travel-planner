using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;
using static TravelPlanner.UnitTests.TestData;

namespace TravelPlanner.UnitTests;

public class FlightFilterTests
{
    private static readonly DateOnly Dep = new(2026, 11, 20);
    private static readonly DateOnly Ret = new(2026, 11, 24);

    [Fact]
    public void Rejects_Expired_And_Zero_Priced_Offers()
    {
        Assert.False(FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 100, expires: Now.AddSeconds(-1)), new TripPreferences(), Now));
        Assert.False(FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 0), new TripPreferences(), Now));
        Assert.True(FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 100), new TripPreferences(), Now));
    }

    [Fact]
    public void Cached_Offers_Without_Expiry_Are_Kept()
    {
        var cached = Flight("TBS", "VIE", Dep, Ret, 100, verification: VerificationStatus.Cached) with { ExpiresAtUtc = null };
        Assert.True(FlightFilter.Matches(cached, new TripPreferences(), Now));
    }

    [Fact]
    public void Applies_Stops_Duration_And_Baggage_Preferences()
    {
        var prefs = new TripPreferences { MaxStops = 0, MaxFlightDurationHours = 4, IncludeCheckedBaggage = true };

        Assert.False(FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 100, stops: 1), prefs, Now));
        Assert.False(FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 100, durationMinutes: 300), prefs, Now));
        Assert.False(FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 100, bags: 0), prefs, Now));
        Assert.True(FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 100, bags: null), prefs, Now)); // unknown is not a violation
        Assert.True(FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 100), prefs, Now));
    }

    [Theory]
    [InlineData(DepartureTimePreference.Morning, 6, true)]
    [InlineData(DepartureTimePreference.Morning, 12, false)]
    [InlineData(DepartureTimePreference.Afternoon, 12, true)]
    [InlineData(DepartureTimePreference.Evening, 21, true)]
    [InlineData(DepartureTimePreference.Night, 23, true)]
    [InlineData(DepartureTimePreference.Night, 3, true)]
    [InlineData(DepartureTimePreference.Night, 9, false)]
    public void Departure_Time_Windows(DepartureTimePreference preference, int hour, bool expected)
    {
        var prefs = new TripPreferences { PreferredDepartureTime = preference };
        Assert.Equal(expected, FlightFilter.Matches(Flight("TBS", "VIE", Dep, Ret, 100, departureHour: hour), prefs, Now));
    }
}

public class PlanScorerTests
{
    private static ScoreInput Input(decimal cost, int flightMinutes = 200, int stops = 0, decimal? stars = 3, int? transferMinutes = 30, bool incomplete = false) =>
        new(cost, 1000m, Flight("TBS", "VIE", new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 24), 100, stops: stops, durationMinutes: flightMinutes),
            Stay("x", 100, stars: stars), transferMinutes is null ? null : TimeSpan.FromMinutes(transferMinutes.Value), incomplete);

    [Fact]
    public void Cheaper_Plan_Scores_Higher_All_Else_Equal()
    {
        var scores = PlanScorer.Score([Input(500), Input(800)], new RankingWeights());
        Assert.True(scores[0].Score > scores[1].Score);
    }

    [Fact]
    public void Shorter_Flights_Score_Higher_All_Else_Equal()
    {
        var scores = PlanScorer.Score([Input(500, flightMinutes: 600, stops: 1), Input(500, flightMinutes: 200)], new RankingWeights());
        Assert.True(scores[1].Score > scores[0].Score);
    }

    [Fact]
    public void Incomplete_Plans_Are_Penalized_And_Breakdown_Is_Transparent()
    {
        var scores = PlanScorer.Score([Input(500), Input(500, incomplete: true)], new RankingWeights());

        Assert.True(scores[0].Score > scores[1].Score);
        Assert.Contains("incompletePenalty", scores[1].Breakdown.Keys);
        Assert.Equal(scores[0].Score, scores[0].Breakdown.Values.Sum());
        Assert.All(new[] { "cost", "flight", "accommodation", "transfer", "budgetRemaining" }, k => Assert.Contains(k, scores[0].Breakdown.Keys));
    }

    [Fact]
    public void Score_Is_Within_0_And_100()
    {
        var scores = PlanScorer.Score([Input(100), Input(999, 900, 2, null, null)], new RankingWeights());
        Assert.All(scores, s => Assert.InRange(s.Score, 0m, 100m));
    }
}

public class OutboundLinkPolicyTests
{
    private readonly IOutboundLinkPolicy _policy = LinkPolicy("booking.com", "aviasales.com");

    [Theory]
    [InlineData("https://www.booking.com/hotel/x.html?aid=1", true)]
    [InlineData("https://booking.com/x", true)]
    [InlineData("https://secure.booking.com/x", true)]
    [InlineData("http://www.booking.com/x", false)]           // not HTTPS
    [InlineData("https://evilbooking.com/x", false)]          // look-alike host
    [InlineData("https://booking.com.evil.net/x", false)]     // suffix trick
    [InlineData("https://user:pw@booking.com/x", false)]      // credentials in URL
    [InlineData("https://booking.com:8443/x", false)]         // non-default port
    [InlineData("javascript:alert(1)", false)]
    [InlineData("/relative/path", false)]
    [InlineData("https://169.254.169.254/latest", false)]     // metadata IP (SSRF-style)
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_Allows_Https_On_Allow_Listed_Hosts(string? url, bool allowed)
    {
        Assert.Equal(allowed, _policy.Sanitize(url) is not null);
    }
}

public class PlanItemDraftFactoryTests
{
    [Fact]
    public void Transfer_Uses_Cheapest_Known_Cost_For_All_Travellers_And_Never_Claims_A_Quote()
    {
        var airport = new Airport("FCO", "Fiumicino", "Rome", "IT", 41.8, 12.24);
        var stay = Stay("h", 100, lat: 41.9, lng: 12.5);
        TransferOption[] options =
        [
            new() { Mode = TransferMode.Taxi, Title = "Taxi", Provider = "P", EstimatedCost = 55m, CostCurrency = "EUR", CostVerification = VerificationStatus.Estimate },
            new() { Mode = TransferMode.PublicTransport, Title = "Train", Provider = "P", EstimatedCost = 14m, CostCurrency = "EUR", CostIsPerPerson = true, CostVerification = VerificationStatus.Estimate }
        ];

        var single = PlanItemDraftFactory.Transfer(TransferDirection.AirportToAccommodation, airport, stay, options, 1, Now);
        var group = PlanItemDraftFactory.Transfer(TransferDirection.AirportToAccommodation, airport, stay, options, 4, Now);

        Assert.Equal(14m, single.Amount);        // train for one person
        Assert.Equal(55m, group.Amount);         // taxi (per vehicle) beats 4 × 14
        Assert.Equal(VerificationStatus.Estimate, group.Verification);
        Assert.Contains("not taxi quotes", group.DetailsJson);
        Assert.Contains("google.com/maps/dir", group.DetailsJson);
    }

    [Fact]
    public void Transfer_Without_Known_Cost_Is_Unavailable_And_Required()
    {
        var airport = new Airport("FCO", "Fiumicino", "Rome", "IT", 41.8, 12.24);
        var draft = PlanItemDraftFactory.Transfer(TransferDirection.AccommodationToAirport, airport, null,
            [new TransferOption { Mode = TransferMode.Information, Title = "Airport site", Provider = "FCO", Url = "https://www.adr.it" }], 1, Now);

        Assert.Null(draft.Amount);
        Assert.True(draft.IsRequired);
        Assert.Equal(VerificationStatus.Unavailable, draft.Verification);
    }
}
