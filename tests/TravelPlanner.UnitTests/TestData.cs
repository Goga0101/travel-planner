using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Domain.ValueObjects;

namespace TravelPlanner.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    public static Airport Tbilisi() => new("TBS", "Tbilisi International Airport", "Tbilisi", "GE", 41.6692, 44.9547) { IsDepartureOption = true };

    public static Destination City(string code, string iata, double lat = 48.0, double lng = 16.0, int popularity = 50)
    {
        var d = new Destination(code, char.ToUpperInvariant(code[0]) + code[1..], "XX", "Testland", lat, lng) { Popularity = popularity };
        d.Airports.Add(new Airport(iata, $"{code} airport", d.City, "XX", lat - 0.1, lng + 0.1) { DestinationId = d.Id });
        return d;
    }

    public static FlightOffer Flight(string from, string to, DateOnly dep, DateOnly ret, decimal price, string currency = "EUR",
        int stops = 0, int durationMinutes = 200, int? bags = 1, int departureHour = 9, DateTimeOffset? expires = null,
        VerificationStatus verification = VerificationStatus.Live, string provider = "FakeAir")
    {
        var duration = TimeSpan.FromMinutes(durationMinutes);
        var outDep = dep.ToDateTime(new TimeOnly(departureHour, 0));
        var inDep = ret.ToDateTime(new TimeOnly(14, 0));
        return new FlightOffer
        {
            Provider = provider,
            Signature = $"{from}{to}{dep}{ret}{price}{departureHour}",
            TotalPrice = price,
            Currency = currency,
            Outbound = new FlightItinerary(from, to, outDep, outDep + duration, duration, stops, [new FlightSegment("XX", "XX1", from, to, outDep, outDep + duration, duration)]),
            Inbound = new FlightItinerary(to, from, inDep, inDep + duration, duration, stops, [new FlightSegment("XX", "XX2", to, from, inDep, inDep + duration, duration)]),
            IncludedCheckedBags = bags,
            Verification = verification,
            RetrievedAtUtc = Now,
            ExpiresAtUtc = expires ?? Now.AddMinutes(30)
        };
    }

    public static AccommodationOffer Stay(string id, decimal price, string currency = "EUR", decimal? stars = 3, decimal? review = 8,
        IReadOnlyList<MandatoryFee>? fees = null, double? lat = 48.0, double? lng = 16.0, string provider = "FakeStay") => new()
    {
        Provider = provider,
        PropertyId = id,
        Name = $"Hotel {id}",
        Latitude = lat,
        Longitude = lng,
        Stars = stars,
        ReviewScore = review,
        TotalPrice = price,
        Currency = currency,
        Fees = fees ?? [],
        Verification = VerificationStatus.Live,
        RetrievedAtUtc = Now,
        ExpiresAtUtc = Now.AddMinutes(30)
    };

    public static PlanItemDraft Line(PlanItemType type, decimal? amount, string? currency = "EUR", bool required = true, bool included = true,
        string? category = null) => new()
    {
        Type = type,
        Category = category ?? CostCategories.For(type),
        Title = $"{type} line",
        Provider = "Test",
        Amount = amount,
        Currency = currency,
        IsRequired = required,
        IncludedInTotal = included,
        RetrievedAtUtc = Now,
        Verification = VerificationStatus.Live
    };

    public static IOutboundLinkPolicy LinkPolicy(params string[] hosts) =>
        new OutboundLinkPolicy(Options.Create(new OutboundLinkOptions { AllowedHosts = hosts.Length > 0 ? [.. hosts] : ["booking.com", "aviasales.com", "google.com"] }));
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class FakeFlightProvider(Func<FlightSearchRequest, IReadOnlyList<FlightOffer>> generate, string name = "FakeAir") : IFlightSearchProvider
{
    private int _calls;
    public int Calls => _calls;
    public List<FlightSearchRequest> Requests { get; } = [];
    public ProviderErrorKind? FailWith { get; set; }
    public string Name => name;
    public bool IsDemo => false;

    public Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests) Requests.Add(request);
        return Task.FromResult(FailWith is { } kind
            ? ProviderResult<IReadOnlyList<FlightOffer>>.Failure(Name, kind, "fail")
            : ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name, generate(request)));
    }
}

internal sealed class FakeAccommodationProvider(Func<AccommodationSearchRequest, IReadOnlyList<AccommodationOffer>> generate, string name = "FakeStay")
    : IAccommodationSearchProvider
{
    private int _calls;
    public int Calls => _calls;
    public List<AccommodationSearchRequest> Requests { get; } = [];
    public string Name => name;
    public bool IsDemo => false;

    public Task<ProviderResult<IReadOnlyList<AccommodationOffer>>> SearchAsync(AccommodationSearchRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        lock (Requests) Requests.Add(request);
        return Task.FromResult(ProviderResult<IReadOnlyList<AccommodationOffer>>.Success(Name, generate(request)));
    }
}

internal sealed class FakeTransportProvider(IReadOnlyList<TransferOption> options) : ITransportSearchProvider
{
    private int _calls;
    public int Calls => _calls;
    public string Name => "FakeRoutes";
    public bool IsDemo => false;

    public Task<ProviderResult<IReadOnlyList<TransferOption>>> SearchAsync(TransportSearchRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(ProviderResult<IReadOnlyList<TransferOption>>.Success(Name, options));
    }
}

internal sealed class FakeRates(params ExchangeRate[] rates) : IExchangeRateProvider
{
    public Task<ExchangeRate?> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken) =>
        Task.FromResult(rates.FirstOrDefault(r => r.From == fromCurrency && r.To == toCurrency));
}

internal sealed class NoSearchLinks : IAccommodationSearchLinkBuilder
{
    public AccommodationSearchLink? Build(string city, string country, DateOnly checkIn, DateOnly checkOut, int adults, int rooms) => null;
}

internal static class EngineFactory
{
    public static TripPlanningEngine Create(
        IEnumerable<IFlightSearchProvider> flights,
        IEnumerable<IAccommodationSearchProvider>? stays = null,
        IEnumerable<ITransportSearchProvider>? transport = null,
        IExchangeRateProvider? rates = null,
        PlanningOptions? options = null,
        BudgetOptions? budget = null,
        IAccommodationSearchLinkBuilder? searchLinks = null) =>
        new(flights, stays ?? [], transport ?? [], rates ?? new FakeRates(), searchLinks ?? new NoSearchLinks(), TestData.LinkPolicy(),
            Options.Create(options ?? new PlanningOptions()), Options.Create(budget ?? new BudgetOptions()),
            new FixedTimeProvider(TestData.Now), NullLogger<TripPlanningEngine>.Instance);

    public static PlanningRequest Request(IReadOnlyList<Destination> destinations, decimal budget = 1000m, int travelers = 1,
        TripPreferences? preferences = null) =>
        new(TestData.Tbilisi(), destinations, new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 30), 5, travelers, budget, "EUR",
            preferences ?? new TripPreferences());

    public static TravelDates Dates(int day) => TravelDates.ForDuration(new DateOnly(2026, 11, day), 5);
}
