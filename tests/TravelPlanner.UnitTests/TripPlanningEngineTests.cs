using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;
using static TravelPlanner.UnitTests.TestData;

namespace TravelPlanner.UnitTests;

public class TripPlanningEngineTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static FakeFlightProvider PricedFlights(Func<string, decimal> priceFor) =>
        new(r => [Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, priceFor(r.DestinationIata))]);

    private static FakeAccommodationProvider Stays(decimal price) =>
        new(r => [Stay($"{r.City}-a", price), Stay($"{r.City}-b", price + 100, stars: 4)]);

    [Fact]
    public async Task Automatic_Mode_Returns_Ranked_Plans_Within_Budget()
    {
        var destinations = new[] { City("vienna", "VIE"), City("rome", "FCO"), City("paris", "CDG") };
        var prices = new Dictionary<string, decimal> { ["VIE"] = 200, ["FCO"] = 250, ["CDG"] = 900 };
        var engine = EngineFactory.Create([PricedFlights(d => prices[d])], [Stays(300)]);

        var result = await engine.PlanAsync(EngineFactory.Request(destinations), null, Ct);

        Assert.True(result.AnyWithinBudget);
        Assert.NotEmpty(result.Plans);
        Assert.All(result.Plans, p => Assert.True(p.Budget.IsAffordable));
        Assert.DoesNotContain(result.Plans, p => p.Destination.Code == "paris"); // 900 flight + 300 stay > 1000
        Assert.Equal("vienna", result.Plans[0].Destination.Code);
        Assert.Equal(result.Plans.OrderByDescending(p => p.Score!.Score).Select(p => p.Score), result.Plans.Select(p => p.Score));
    }

    [Fact]
    public async Task Manual_Mode_Only_Searches_Selected_Destinations()
    {
        var flights = PricedFlights(_ => 200);
        var engine = EngineFactory.Create([flights], [Stays(300)]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        Assert.All(flights.Requests, r => Assert.Equal("FCO", r.DestinationIata));
        Assert.All(result.Plans, p => Assert.Equal("rome", p.Destination.Code));
    }

    [Fact]
    public async Task Plans_Use_Five_Days_And_Four_Nights()
    {
        var stays = Stays(300);
        var engine = EngineFactory.Create([PricedFlights(_ => 200)], [stays]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        Assert.All(stays.Requests, r => Assert.Equal(4, r.Nights));
        Assert.All(result.Plans, p => Assert.Equal(4, p.Dates.Nights));
    }

    [Fact]
    public async Task Accommodation_Is_Searched_Once_Per_Destination_And_Dates()
    {
        var flights = new FakeFlightProvider(r =>
        [
            Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 200, departureHour: 7),
            Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 220, departureHour: 12),
            Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 260, departureHour: 18)
        ]);
        var stays = Stays(300);
        var options = new PlanningOptions { MaxCombosPerDestinationForAccommodation = 2, MaxFlightsPerCandidate = 3 };
        var engine = EngineFactory.Create([flights], [stays], options: options);

        await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO"), City("vienna", "VIE")]), null, Ct);

        var keys = stays.Requests.Select(r => r.CacheKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.True(stays.Calls <= 2 * 2); // destinations × MaxCombosPerDestinationForAccommodation
    }

    [Fact]
    public async Task Flight_Requests_Are_Capped()
    {
        var flights = PricedFlights(_ => 200);
        var destinations = Enumerable.Range(0, 10).Select(i => City($"city{i}", $"C{i:D2}")).ToList();
        var engine = EngineFactory.Create([flights], [Stays(300)], options: new PlanningOptions { MaxFlightSearches = 15 });

        var result = await engine.PlanAsync(EngineFactory.Request(destinations), null, Ct);

        Assert.True(flights.Calls <= 15);
        Assert.Contains(result.Warnings, w => w.StartsWith("Request limit"));
    }

    [Fact]
    public async Task Over_Budget_Returns_Labelled_Fallback_Not_Affordable_Plans()
    {
        var engine = EngineFactory.Create([PricedFlights(_ => 700)], [Stays(600)]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        Assert.False(result.AnyWithinBudget);
        Assert.NotEmpty(result.Plans);
        Assert.All(result.Plans, p => Assert.Equal(BudgetFitStatus.OverBudget, p.Budget.Fit));
    }

    [Fact]
    public async Task Flights_Alone_Over_Budget_Skip_Accommodation_Search()
    {
        var stays = Stays(100);
        var engine = EngineFactory.Create([PricedFlights(_ => 1500)], [stays]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        Assert.Equal(0, stays.Calls);
        Assert.False(result.AnyWithinBudget);
        Assert.All(result.Plans, p => Assert.Equal(BudgetFitStatus.OverBudget, p.Budget.Fit));
    }

    [Fact]
    public async Task Expired_Flight_Offers_Are_Ignored()
    {
        var flights = new FakeFlightProvider(r => [Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 100, expires: Now.AddMinutes(-1))]);
        var engine = EngineFactory.Create([flights], [Stays(100)]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        Assert.Empty(result.Plans);
        Assert.Contains(result.Warnings, w => w.Contains("No round-trip flights"));
    }

    [Fact]
    public async Task Missing_Accommodation_Provider_Produces_Incomplete_Plans()
    {
        var engine = EngineFactory.Create([PricedFlights(_ => 200)]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        Assert.NotEmpty(result.Plans);
        Assert.All(result.Plans, p =>
        {
            Assert.Equal(BudgetFitStatus.WithinBudgetIncomplete, p.Budget.Fit);
            Assert.Contains(CostCategories.Accommodation, p.Budget.MissingCategories);
            Assert.Contains(p.Drafts, d => d is { Type: PlanItemType.Accommodation, Verification: VerificationStatus.Unavailable });
        });
        Assert.Contains(result.Warnings, w => w.Contains("No accommodation provider"));
    }

    [Fact]
    public async Task No_Flight_Provider_Fails_With_Clear_Message()
    {
        var engine = EngineFactory.Create([]);

        var ex = await Assert.ThrowsAsync<PlanningException>(() => engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct));
        Assert.Contains("No flight data provider", ex.Message);
    }

    [Fact]
    public async Task Provider_Failures_Become_Aggregated_Warnings()
    {
        var failing = new FakeFlightProvider(_ => [], "BrokenAir") { FailWith = ProviderErrorKind.RateLimited };
        var engine = EngineFactory.Create([failing, PricedFlights(_ => 200)], [Stays(200)]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        Assert.NotEmpty(result.Plans);
        Assert.Single(result.Warnings, w => w.Contains("BrokenAir") && w.Contains("rate-limited"));
    }

    [Fact]
    public async Task Preferences_Filter_Flights_And_Accommodation()
    {
        var flights = new FakeFlightProvider(r =>
        [
            Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 150, stops: 2),
            Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 300, stops: 0)
        ]);
        var stays = new FakeAccommodationProvider(_ => [Stay("cheap", 100, stars: 2), Stay("nice", 300, stars: 4)]);
        var engine = EngineFactory.Create([flights], [stays]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")],
            preferences: new TripPreferences { MaxStops = 0, MinAccommodationStars = 4 }), null, Ct);

        Assert.All(result.Plans, p =>
        {
            Assert.Equal(0, p.Flight.MaxStops);
            Assert.Equal("nice", p.Accommodation!.PropertyId);
        });
    }

    [Fact]
    public async Task Transfers_Are_Added_For_Finalists_With_Cheapest_Known_Cost()
    {
        var transport = new FakeTransportProvider(
        [
            new TransferOption { Mode = TransferMode.PublicTransport, Title = "Bus", Provider = "FakeRoutes", Duration = TimeSpan.FromMinutes(40),
                EstimatedCost = 3m, CostCurrency = "EUR", CostIsPerPerson = true, CostVerification = VerificationStatus.Estimate },
            new TransferOption { Mode = TransferMode.Drive, Title = "Road", Provider = "FakeRoutes", Duration = TimeSpan.FromMinutes(25), DistanceMeters = 20000 }
        ]);
        var engine = EngineFactory.Create([PricedFlights(_ => 200)], [Stays(300)], [transport]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")], travelers: 2), null, Ct);

        var plan = result.Plans[0];
        var transfers = plan.Drafts.Where(d => d.Type == PlanItemType.Transfer).ToList();
        Assert.Equal(2, transfers.Count);
        Assert.All(transfers, t => Assert.Equal(6m, t.Amount)); // 3 EUR × 2 travellers
        Assert.Equal(BudgetFitStatus.WithinBudget, plan.Budget.Fit);
        Assert.Equal(TimeSpan.FromMinutes(25), plan.TransferDuration);
    }

    [Fact]
    public async Task Prices_In_Other_Currency_Without_Rate_Are_Not_Treated_As_Affordable()
    {
        var flights = new FakeFlightProvider(r => [Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 200, currency: "USD")]);
        var engine = EngineFactory.Create([flights], [Stays(300)]);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        Assert.Empty(result.Plans);
        Assert.Contains(result.Warnings, w => w.Contains("could not be converted"));
    }

    [Fact]
    public async Task Prices_In_Other_Currency_Are_Converted_With_Recorded_Rate()
    {
        var flights = new FakeFlightProvider(r => [Flight(r.OriginIata, r.DestinationIata, r.DepartureDate, r.ReturnDate, 200, currency: "USD")]);
        var rates = new FakeRates(new ExchangeRate("USD", "EUR", 0.5m, "ECB test", Now));
        var engine = EngineFactory.Create([flights], [Stays(300)], rates: rates);

        var result = await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), null, Ct);

        var flightLine = result.Plans[0].Budget.Lines.Single(l => l.Draft.Type == PlanItemType.Flight);
        Assert.Equal(100m, flightLine.ConvertedAmount);
        Assert.Equal("ECB test", flightLine.Rate!.Source);
    }

    [Fact]
    public async Task Reports_Progress()
    {
        var reports = new List<int>();
        var engine = EngineFactory.Create([PricedFlights(_ => 200)], [Stays(300)]);

        await engine.PlanAsync(EngineFactory.Request([City("rome", "FCO")]), p => { lock (reports) reports.Add(p.Percent); return Task.CompletedTask; }, Ct);

        Assert.Contains(95, reports);
        Assert.All(reports, p => Assert.InRange(p, 0, 100));
    }
}
