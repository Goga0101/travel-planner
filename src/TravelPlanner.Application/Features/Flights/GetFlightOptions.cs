using FluentValidation;
using MediatR;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.Flights;

/// <summary>
/// The flights for chosen dates: one way when Return is null, otherwise round trips — single round-trip tickets, two
/// one-way tickets, and one-way tickets combined through a connecting city — each with its price and booking link(s).
/// </summary>
public sealed record GetFlightOptionsQuery(string Origin, string Destination, DateOnly Departure, DateOnly? Return, int Adults = 1, string Currency = "EUR")
    : IRequest<FlightOptionsDto>;

/// <param name="SearchUrl">A live search for the same dates on the fare site, for flights the cached fares miss.</param>
public sealed record FlightOptionsDto(bool Available, string Currency, int Adults, IReadOnlyList<FlightOptionDto> Options, string? SearchUrl = null);

/// <param name="Kind">OneWay, RoundTrip (one ticket both ways), TwoOneWays (a ticket each way) or SelfTransfer (a change on separate tickets).</param>
/// <param name="Price">Total for all travellers.</param>
/// <param name="Tickets">The separately booked tickets, each with its own link (empty for a single ticket).</param>
public sealed record FlightOptionDto(
    string Kind,
    decimal Price,
    string Currency,
    FlightLegSummaryDto Outbound,
    FlightLegSummaryDto? Inbound,
    string? BookingUrl,
    IReadOnlyList<FlightTicket> Tickets);

public sealed class GetFlightOptionsValidator : AbstractValidator<GetFlightOptionsQuery>
{
    public GetFlightOptionsValidator(IWorldPlaces world, TimeProvider timeProvider)
    {
        RuleFor(x => x.Origin).Must(c => c is { Length: 3 } && world.FindCity(c) is not null).WithMessage(_ => T.Get("val.cityUnknown"));
        RuleFor(x => x.Destination).Must(c => c is { Length: 3 } && world.FindCity(c) is not null).WithMessage(_ => T.Get("val.cityUnknown"));
        RuleFor(x => x.Departure)
            .Must(d => d >= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(-1)).WithMessage(_ => T.Get("val.departureFuture"))
            .Must(d => d <= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(366)).WithMessage(_ => T.Get("val.departureWithin", 366));
        RuleFor(x => x.Return).Must((q, r) => r is null || r >= q.Departure).WithMessage(_ => T.Get("val.returnAfter"));
        RuleFor(x => x.Adults).InclusiveBetween(1, 9).WithMessage(_ => T.Get("val.travelers", 1, 9));
        RuleFor(x => x.Currency).Must(c => BudgetOptions.SupportedCurrencies.Contains(c?.ToUpperInvariant()))
            .WithMessage(_ => T.Get("val.currency", string.Join(", ", BudgetOptions.SupportedCurrencies)));
    }
}

public sealed class GetFlightOptionsHandler(
    IFlightCalendarSource calendar,
    IEnumerable<IFlightSearchProvider> providers,
    IWorldPlaces world,
    IApplicationDbContext db) : IRequestHandler<GetFlightOptionsQuery, FlightOptionsDto>
{
    private const int MaxOptions = 40;

    public async Task<FlightOptionsDto> Handle(GetFlightOptionsQuery request, CancellationToken cancellationToken)
    {
        var currency = request.Currency.ToUpperInvariant();
        if (!calendar.IsAvailable) return new FlightOptionsDto(false, currency, request.Adults, []);
        var origin = request.Origin.ToUpperInvariant();
        var destination = request.Destination.ToUpperInvariant();
        var searchUrl = calendar.SearchLink(origin, destination, request.Departure, request.Return, request.Adults);

        if (request.Return is not { } back)
        {
            var fares = await calendar.FaresOnAsync(origin, destination, request.Departure, currency, cancellationToken);
            var lookup = await AirportLookup.LoadAsync(db, fares.SelectMany(f => (f.Via ?? []).Append(f.From).Append(f.To)), cancellationToken);
            return new FlightOptionsDto(true, currency, request.Adults, fares.Take(MaxOptions)
                .Select(f => new FlightOptionDto("OneWay", f.Price * request.Adults, f.Currency, OneWayLeg(f, lookup), null, f.BookingUrl, []))
                .ToList(), searchUrl);
        }

        // Round trips: whole tickets from window-capable providers, plus separate one-way tickets (direct or through a hub).
        var window = new FlightWindowRequest(origin, destination, request.Departure, back, back.DayNumber - request.Departure.DayNumber + 1,
            request.Adults, currency, null, false, MaxOptions);
        var searches = new List<Task<ProviderResult<IReadOnlyList<FlightOffer>>>>();
        foreach (var provider in providers.Where(p => !p.IsDemo))
        {
            if (provider is IFlightWindowSearchProvider windowProvider) searches.Add(windowProvider.SearchWindowAsync(window, cancellationToken));
            if (provider is ISelfTransferFlightProvider selfTransfer) searches.Add(selfTransfer.SearchSelfTransferAsync(window, cancellationToken));
        }
        var offers = (await Task.WhenAll(searches))
            .Where(r => r.Succeeded)
            .SelectMany(r => r.Data!)
            .Where(o => DateOnly.FromDateTime(o.Outbound.DepartureLocal) == request.Departure && DateOnly.FromDateTime(o.Inbound.DepartureLocal) == back)
            .DistinctBy(o => (o.Provider, o.Signature))
            .OrderBy(o => o.TotalPrice)
            .Take(MaxOptions)
            .ToList();
        var airports = await AirportLookup.LoadAsync(db,
            offers.SelectMany(o => AirportLookup.CodesOf(o.Outbound).Concat(AirportLookup.CodesOf(o.Inbound))), cancellationToken);

        return new FlightOptionsDto(true, currency, request.Adults, offers.Select(o =>
        {
            var kind = o.Tickets.Count == 0 ? "RoundTrip" : FlightTicket.IsSelfTransfer(o.Tickets) ? "SelfTransfer" : "TwoOneWays";
            return new FlightOptionDto(kind, o.TotalPrice, o.Currency, Named(DtoMapper.Leg(o.Outbound, o.ValidatingCarrier, airports)),
                Named(DtoMapper.Leg(o.Inbound, o.ValidatingCarrier, airports)), o.BookingUrl, o.Tickets);
        }).ToList(), searchUrl);
    }

    /// <summary>A one-way fare as a leg; its arrival is converted to the arrival airport's time zone when that is known.</summary>
    private FlightLegSummaryDto OneWayLeg(CalendarFare f, AirportLookup airports)
    {
        DateTime? arrival = null;
        if (f.Duration is { } duration && f.DepartureOffset is { } offset)
        {
            var arrivalUtc = new DateTimeOffset(f.DepartureLocal, offset).Add(duration);
            arrival = world.FindCity(f.To)?.TimeZone is { } zone && TryZone(zone) is { } tz
                ? TimeZoneInfo.ConvertTime(arrivalUtc, tz).DateTime
                : null;
        }
        return Named(new FlightLegSummaryDto(f.From, f.To, f.DepartureLocal, arrival, f.Duration is { } d ? (int)d.TotalMinutes : null, f.Stops,
            f.Airline is { Length: > 0 } a ? [a] : [], airports.CityOf(f.From), airports.CityOf(f.To), (f.Via ?? []).Select(airports.Stop).ToList()));
    }

    /// <summary>Airports outside the catalog get their city from the world airport list.</summary>
    private FlightLegSummaryDto Named(FlightLegSummaryDto leg) => leg with
    {
        FromCity = leg.FromCity ?? world.FindCity(leg.From)?.City,
        ToCity = leg.ToCity ?? world.FindCity(leg.To)?.City,
        Via = leg.Via.Select(v => v.City == v.Iata && world.FindCity(v.Iata) is { } c ? v with { City = c.City, CountryCode = c.CountryCode } : v).ToList()
    };

    private static TimeZoneInfo? TryZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }
}
