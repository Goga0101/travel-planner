using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Infrastructure.Catalog;

namespace TravelPlanner.Infrastructure.Providers.Demo;

// DEMO PROVIDERS — generate deterministic, clearly fictitious data so the full workflow can be exercised without
// credentials. Every offer is marked VerificationStatus.Demo, names carry a [DEMO] prefix, carriers use the
// non-airline codes Z1–Z4, and no booking links are produced. They are only registered when Providers:Mode = Demo.

internal static class DemoMath
{
    public const string ProviderName = Application.Dtos.DtoMapper.DemoProviderName;

    /// <summary>Stable FNV-1a hash (string.GetHashCode is randomized per process).</summary>
    public static uint Hash(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value) hash = (hash ^ c) * 16777619u;
        return hash;
    }

    /// <summary>Deterministic value in [0, 1).</summary>
    public static double Unit(string seed) => Hash(seed) % 10_000 / 10_000d;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }

    public static decimal Money(double value) => Math.Round((decimal)value, 2, MidpointRounding.AwayFromZero);
}

public sealed class DemoFlightSearchProvider(CatalogFileReader catalog, TimeProvider timeProvider) : IFlightSearchProvider
{
    private static readonly TimeOnly[] DepartureSlots = [new(6, 10), new(9, 45), new(13, 20), new(17, 55), new(22, 30)];
    private static readonly string[] Carriers = ["Z1", "Z2", "Z3", "Z4"];

    public string Name => DemoMath.ProviderName;
    public bool IsDemo => true;

    public Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken)
    {
        var airports = catalog.Document.AllAirports.ToDictionary(a => a.Iata.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
        if (!airports.TryGetValue(request.OriginIata, out var origin) || !airports.TryGetValue(request.DestinationIata, out var destination))
            return Task.FromResult(ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name, []));

        var now = timeProvider.GetUtcNow();
        var distance = DemoMath.DistanceKm(origin.Latitude, origin.Longitude, destination.Latitude, destination.Longitude);
        var route = $"{request.OriginIata}-{request.DestinationIata}";
        var offers = new List<FlightOffer>();

        for (var i = 0; i < 4; i++)
        {
            var seed = $"{route}|{request.DepartureDate}|{request.ReturnDate}|{i}";
            var stops = distance < 2600 && DemoMath.Unit(seed + "s") > 0.35 ? 0 : 1;
            if (request.MaxStops.HasValue && stops > request.MaxStops) continue;
            var bags = DemoMath.Unit(seed + "b") > 0.5 ? 1 : 0;
            if (request.RequireCheckedBag && bags == 0) continue;

            var hours = distance / 780 + 0.6 + stops * (1.5 + DemoMath.Unit(seed + "l") * 2);
            var duration = TimeSpan.FromMinutes(Math.Round(hours * 60 / 5) * 5);
            var weekend = request.DepartureDate.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? 1.15 : 1.0;
            var perPerson = (55 + distance * 0.085) * (0.75 + DemoMath.Unit(seed + "p") * 0.8) * weekend * (stops == 0 ? 1.15 : 1.0) + bags * 30;
            var carrier = Carriers[DemoMath.Hash(seed) % Carriers.Length];

            var outDep = request.DepartureDate.ToDateTime(DepartureSlots[DemoMath.Hash(seed + "o") % DepartureSlots.Length]);
            var inDep = request.ReturnDate.ToDateTime(DepartureSlots[DemoMath.Hash(seed + "i") % DepartureSlots.Length]);
            var outbound = Itinerary(carrier, request.OriginIata, request.DestinationIata, outDep, duration, stops, seed + "out");
            var inbound = Itinerary(carrier, request.DestinationIata, request.OriginIata, inDep, duration, stops, seed + "in");

            offers.Add(new FlightOffer
            {
                Provider = Name,
                Signature = $"DEMO|{string.Join("|", outbound.Segments.Concat(inbound.Segments).Select(s => $"{s.FlightNumber}@{s.DepartureLocal:yyyyMMddHHmm}"))}",
                TotalPrice = DemoMath.Money(perPerson * request.Adults),
                Currency = request.Currency,
                Outbound = outbound,
                Inbound = inbound,
                IncludedCheckedBags = bags,
                ValidatingCarrier = carrier,
                BookingUrl = null,
                SourceNote = T.Get("demo.flightNote"),
                Verification = VerificationStatus.Demo,
                RetrievedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(30)
            });
        }

        return Task.FromResult(ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name, offers));
    }

    private static FlightItinerary Itinerary(string carrier, string from, string to, DateTime departure, TimeSpan duration, int stops, string seed)
    {
        var number = 100 + (int)(DemoMath.Hash(seed) % 800);
        if (stops == 0)
        {
            var segment = new FlightSegment(carrier, $"{carrier}{number}", from, to, departure, departure + duration, duration);
            return new FlightItinerary(from, to, departure, departure + duration, duration, 0, [segment]);
        }

        const string hub = "XHB"; // fictitious hub code used only by demo data
        var first = TimeSpan.FromMinutes(Math.Round(duration.TotalMinutes * 0.35));
        var layover = TimeSpan.FromMinutes(75);
        var second = duration - first - layover;
        var s1 = new FlightSegment(carrier, $"{carrier}{number}", from, hub, departure, departure + first, first);
        var s2 = new FlightSegment(carrier, $"{carrier}{number + 1}", hub, to, departure + first + layover, departure + duration, second);
        return new FlightItinerary(from, to, departure, departure + duration, duration, 1, [s1, s2]);
    }
}

public sealed class DemoAccommodationProvider(TimeProvider timeProvider) : IAccommodationSearchProvider
{
    private static readonly (string Name, int Stars, double NightlyBase)[] Templates =
    [
        ("City Hostel", 2, 38), ("Budget Inn", 2, 52), ("Central Hotel", 3, 78), ("Riverside Apartments", 3, 92),
        ("Boutique Stay", 4, 125), ("Grand Hotel", 5, 230)
    ];

    public string Name => DemoMath.ProviderName;
    public bool IsDemo => true;

    public Task<ProviderResult<IReadOnlyList<AccommodationOffer>>> SearchAsync(AccommodationSearchRequest request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var cityFactor = 0.8 + DemoMath.Unit(request.DestinationId + "price") * 0.6;
        var offers = new List<AccommodationOffer>();

        foreach (var (name, stars, nightlyBase) in Templates)
        {
            if (request.MinStars.HasValue && stars < request.MinStars) continue;
            var seed = $"{request.DestinationId}|{name}";
            var nightly = nightlyBase * cityFactor * (0.9 + DemoMath.Unit($"{seed}|{request.CheckIn}") * 0.25) * request.Rooms;
            var total = DemoMath.Money(nightly * request.Nights);

            // Alternate fee handling so both "included" and "payable on site" paths are visible in demo mode.
            var fees = new List<MandatoryFee>();
            if (stars >= 3 && DemoMath.Unit(seed + "tax") > 0.4)
                fees.Add(new MandatoryFee(T.Get("demo.cityTax"), DemoMath.Money(2.5 * request.Adults * request.Nights), request.Currency, IncludedInPrice: false, PayableAtProperty: true));
            else
                fees.Add(new MandatoryFee(T.Get("demo.vat"), DemoMath.Money((double)total * 0.09), request.Currency, IncludedInPrice: true, PayableAtProperty: false));

            var angle = DemoMath.Unit(seed + "a") * 2 * Math.PI;
            var radiusKm = 0.3 + DemoMath.Unit(seed + "r") * 2.7;
            var lat = request.Latitude + radiusKm / 111 * Math.Cos(angle);
            var lng = request.Longitude + radiusKm / (111 * Math.Cos(request.Latitude * Math.PI / 180)) * Math.Sin(angle);
            var freeCancel = DemoMath.Unit(seed + "c") > 0.4;

            offers.Add(new AccommodationOffer
            {
                Provider = Name,
                PropertyId = $"demo-{DemoMath.Hash(seed):x8}",
                Name = $"[DEMO] {request.City} {name}",
                Address = T.Get("demo.address", radiusKm, request.City),
                Latitude = Math.Round(lat, 6),
                Longitude = Math.Round(lng, 6),
                Stars = stars,
                ReviewScore = Math.Round((decimal)(6.6 + DemoMath.Unit(seed + "rev") * 2.9), 1),
                Category = T.Get(stars <= 2 ? "demo.categoryBudget" : name.Contains("Apartments") ? "demo.categoryApartment" : "demo.categoryHotel"),
                TotalPrice = total,
                Currency = request.Currency,
                Fees = fees,
                CancellationPolicy = T.Get(freeCancel ? "demo.freeCancellation" : "demo.nonRefundable"),
                FreeCancellationUntil = freeCancel ? new DateTimeOffset(request.CheckIn.AddDays(-2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null,
                MealPlan = stars >= 4 ? T.Get("demo.breakfast") : null,
                BookingUrl = null,
                Verification = VerificationStatus.Demo,
                RetrievedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(30)
            });
        }

        return Task.FromResult(ProviderResult<IReadOnlyList<AccommodationOffer>>.Success(Name, offers));
    }
}

public sealed class DemoTransportProvider : ITransportSearchProvider
{
    public string Name => DemoMath.ProviderName;
    public bool IsDemo => true;

    public Task<ProviderResult<IReadOnlyList<TransferOption>>> SearchAsync(TransportSearchRequest request, CancellationToken cancellationToken)
    {
        var km = DemoMath.DistanceKm(request.AirportLatitude, request.AirportLongitude, request.AccommodationLatitude, request.AccommodationLongitude) * 1.3;
        var seed = $"{request.AirportIata}|{request.AccommodationLatitude:F3}";
        var fare = DemoMath.Money(2 + DemoMath.Unit(seed) * 8);

        IReadOnlyList<TransferOption> options =
        [
            new TransferOption
            {
                Mode = TransferMode.PublicTransport,
                Title = T.Get("demo.publicTransport"),
                Provider = Name,
                DistanceMeters = (int)(km * 1000),
                Duration = TimeSpan.FromMinutes(Math.Round(km / 25 * 60 + 15)),
                EstimatedCost = fare,
                CostCurrency = request.Currency,
                CostIsPerPerson = true,
                CostVerification = VerificationStatus.Demo,
                InfoVerification = VerificationStatus.Demo,
                Lines = [T.Get("demo.airportExpress")],
                Notes = T.Get("demo.routeNote")
            },
            new TransferOption
            {
                Mode = TransferMode.Taxi,
                Title = T.Get("demo.taxi"),
                Provider = Name,
                DistanceMeters = (int)(km * 1000),
                Duration = TimeSpan.FromMinutes(Math.Round(km / 45 * 60 + 10)),
                EstimatedCost = DemoMath.Money(4 + 1.6 * km),
                CostCurrency = request.Currency,
                CostIsPerPerson = false,
                CostVerification = VerificationStatus.Demo,
                InfoVerification = VerificationStatus.Demo,
                Notes = T.Get("demo.taxiNote")
            }
        ];
        return Task.FromResult(ProviderResult<IReadOnlyList<TransferOption>>.Success(Name, options));
    }
}
