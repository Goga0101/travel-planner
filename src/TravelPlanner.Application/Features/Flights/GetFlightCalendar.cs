using System.Globalization;
using FluentValidation;
using MediatR;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.Flights;

/// <summary>
/// When can I fly from A to B? The cheapest known one-way fare of each day over the coming months, without fixed dates.
/// Codes are city or airport codes. Nearby airports are tried too, since cached fares are sparse for smaller airports
/// (San Diego â†’ Buenos Aires has none, Los Angeles â†’ Buenos Aires has plenty).
/// </summary>
public sealed record GetFlightCalendarQuery(string Origin, string Destination, string Currency = "EUR", int Months = 12)
    : IRequest<FlightCalendarDto>;

/// <param name="Available">False when no fare source is configured (demo mode).</param>
public sealed record FlightCalendarDto(bool Available, string Currency, IReadOnlyList<FlightCalendarRouteDto> Routes);

/// <param name="IsRequested">The route as asked; otherwise one end is a nearby city, FromKm/ToKm away from the one asked.</param>
public sealed record FlightCalendarRouteDto(
    string FromCode,
    string FromCity,
    string ToCode,
    string ToCity,
    bool IsRequested,
    int? FromKm,
    int? ToKm,
    IReadOnlyList<FlightCalendarDayDto> Days);

/// <summary>Price is per passenger; Time is the airport-local departure time (HH:mm).</summary>
public sealed record FlightCalendarDayDto(DateOnly Date, string Time, string From, string To, int Stops, string? Airline, decimal Price, string? BookingUrl);

public sealed class GetFlightCalendarValidator : AbstractValidator<GetFlightCalendarQuery>
{
    public GetFlightCalendarValidator(IWorldPlaces world)
    {
        RuleFor(x => x.Origin).Must(c => Known(world, c)).WithMessage(_ => T.Get("val.cityUnknown"));
        RuleFor(x => x.Destination).Must(c => Known(world, c)).WithMessage(_ => T.Get("val.cityUnknown"));
        RuleFor(x => x.Currency).Must(c => BudgetOptions.SupportedCurrencies.Contains(c?.ToUpperInvariant()))
            .WithMessage(_ => T.Get("val.currency", string.Join(", ", BudgetOptions.SupportedCurrencies)));
        RuleFor(x => x.Months).InclusiveBetween(1, 12);
    }

    private static bool Known(IWorldPlaces world, string? code) => code is { Length: 3 } && world.FindCity(code) is not null;
}

public sealed class GetFlightCalendarHandler(IFlightCalendarSource source, IWorldPlaces world, TimeProvider timeProvider)
    : IRequestHandler<GetFlightCalendarQuery, FlightCalendarDto>
{
    private const double NearbyKm = 300;
    private const int NearbyCandidates = 10;
    private const int MaxNearbyOrigins = 2;
    private const int MaxNearbyDestinations = 1;

    public async Task<FlightCalendarDto> Handle(GetFlightCalendarQuery request, CancellationToken cancellationToken)
    {
        var currency = request.Currency.ToUpperInvariant();
        if (!source.IsAvailable) return new FlightCalendarDto(false, currency, []);

        var from = world.FindCity(request.Origin)!;
        var to = world.FindCity(request.Destination)!;
        // An airport asked for stays that airport (SAN, not the city code); a city code covers all its airports.
        var fromCode = request.Origin.ToUpperInvariant();
        var toCode = request.Destination.ToUpperInvariant();

        var nearbyFrom = NearbyAsync(from, to, toCode, fromEnd: true, MaxNearbyOrigins, currency, cancellationToken);
        var nearbyTo = NearbyAsync(to, from, fromCode, fromEnd: false, MaxNearbyDestinations, currency, cancellationToken);
        var pairs = new List<(WorldCity From, string FromCode, WorldCity To, string ToCode, int? FromKm, int? ToKm)> { (from, fromCode, to, toCode, null, null) };
        pairs.AddRange((await nearbyFrom).Select(n => (n.City, n.City.CityCode, to, toCode, (int?)n.Km, (int?)null)));
        pairs.AddRange((await nearbyTo).Select(n => (from, fromCode, n.City, n.City.CityCode, (int?)null, (int?)n.Km)));

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var months = Enumerable.Range(0, request.Months).Select(i => today.AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture)).ToList();

        var routes = await Task.WhenAll(pairs.Select(async (p, i) =>
        {
            var fares = await Task.WhenAll(months.Select(m => source.CheapestByDayAsync(p.FromCode, p.ToCode, m, currency, cancellationToken)));
            var days = fares.SelectMany(f => f)
                .Where(f => DateOnly.FromDateTime(f.DepartureLocal) >= today)
                .Select(f => new FlightCalendarDayDto(DateOnly.FromDateTime(f.DepartureLocal), f.DepartureLocal.ToString("HH:mm", CultureInfo.InvariantCulture), f.From, f.To,
                    f.Stops, f.Airline, f.Price, f.BookingUrl))
                .DistinctBy(d => d.Date)
                .OrderBy(d => d.Date)
                .ToList();
            return new FlightCalendarRouteDto(p.FromCode, p.From.City, p.ToCode, p.To.City, i == 0, p.FromKm, p.ToKm, days);
        }));

        // The route asked for always comes first (even when empty, so the page can say so); nearby ones only when they have fares.
        return new FlightCalendarDto(true, currency, routes.Where((r, i) => i == 0 || r.Days.Count > 0).ToList());
    }

    /// <summary>Cities near one end of the route from which (or to which) any fare is known, nearest first.</summary>
    private async Task<IReadOnlyList<(WorldCity City, int Km)>> NearbyAsync(WorldCity end, WorldCity other, string otherCode, bool fromEnd, int max,
        string currency, CancellationToken cancellationToken)
    {
        var candidates = world.CitiesNear(end.Latitude, end.Longitude, NearbyKm, NearbyCandidates + 1)
            .Where(c => c.City.CityCode != end.CityCode && c.City.CityCode != other.CityCode)
            .Take(NearbyCandidates)
            .ToList();
        var known = await Task.WhenAll(candidates.Select(c => fromEnd
            ? source.HasFaresAsync(c.City.CityCode, otherCode, currency, cancellationToken)
            : source.HasFaresAsync(otherCode, c.City.CityCode, currency, cancellationToken)));
        return candidates.Where((_, i) => known[i]).Take(max).Select(c => (c.City, (int)Math.Round(c.Km))).ToList();
    }
}
