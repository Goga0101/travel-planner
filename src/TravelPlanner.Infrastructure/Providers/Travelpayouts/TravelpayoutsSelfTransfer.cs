using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Infrastructure.Providers.Travelpayouts;

/// <summary>A cached one-way fare. Times keep the departure airport's offset; Arrival is departure + flight time.</summary>
public sealed record OneWayFare(
    string From,
    string To,
    string FromCity,
    string ToCity,
    DateTimeOffset Departure,
    DateTimeOffset? Arrival,
    int Transfers,
    string? Airline,
    decimal Price,
    string? Link,
    IReadOnlyList<string> Via,
    string Signature);

/// <summary>
/// Self-transfer search: low-cost fares are often cheapest as separate one-way tickets, directly or with a change in a
/// connecting city. Connecting cities are discovered from the cheapest fares leaving the origin and reaching the
/// destination (the Data API accepts a missing origin or destination), topped up with well-known hubs, and then priced
/// month by month. One-way fares are shared between destinations through the memory cache.
/// </summary>
public sealed partial class TravelpayoutsFlightProvider
{
    private const int MaxHubs = 3;
    private const int OptionsPerDay = 3;
    private static readonly TimeSpan MinConnection = TimeSpan.FromHours(3);
    private static readonly TimeSpan MinConnectionAirportChange = TimeSpan.FromHours(5);
    private static readonly TimeSpan MaxConnection = TimeSpan.FromHours(24);
    private static readonly TimeSpan FareCacheTtl = TimeSpan.FromMinutes(30);

    /// <summary>City codes of big connecting hubs (low-cost bases and long-haul gateways), tried when discovery finds too few.</summary>
    private static readonly string[] KnownHubs =
        ["IST", "MIL", "BUD", "VIE", "WAW", "LON", "BER", "ATH", "DXB", "DOH", "AUH", "SHJ", "SIN", "BKK", "KUL", "DEL"];

    private static readonly SemaphoreSlim Throttle = new(6);

    public Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchSelfTransferAsync(FlightWindowRequest request, CancellationToken cancellationToken) =>
        ProviderHttp.Guard(Name, async () =>
        {
            var lastDeparture = request.LatestReturn.AddDays(-(request.DurationDays - 1));
            var departureDays = Days(request.EarliestDeparture, lastDeparture);
            if (departureDays.Count == 0) return ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name, []);
            var returnDays = departureDays.Select(d => d.AddDays(request.DurationDays - 1)).ToList();
            var origin = request.OriginIata;
            var destination = request.DestinationIata;
            var direct = request.MaxStops is <= 1 ? 0 : (int?)null; // with at most one change, each ticket must be non-stop

            // 1. Connecting cities: cheap from the origin and cheap to the destination (and back).
            var outboundHubs = await HubsAsync(origin, destination, Month(departureDays[0]), request.Currency, cancellationToken);
            var returnHubs = await HubsAsync(destination, origin, Month(returnDays[0]), request.Currency, cancellationToken);

            // 2. One-way fares by month: the direct ticket and both tickets through each hub (the second leg may leave a day later).
            var outMonths = Months(departureDays, extraDay: true);
            var backMonths = Months(returnDays, extraDay: true);
            var routes = new List<(string From, string To, IReadOnlyList<string> Months)> { (origin, destination, outMonths), (destination, origin, backMonths) };
            foreach (var hub in outboundHubs) { routes.Add((origin, hub, outMonths)); routes.Add((hub, destination, outMonths)); }
            foreach (var hub in returnHubs) { routes.Add((destination, hub, backMonths)); routes.Add((hub, origin, backMonths)); }

            var fares = new ConcurrentDictionary<(string From, string To), List<OneWayFare>>();
            await Task.WhenAll(routes.Select(async r =>
            {
                var list = new List<OneWayFare>();
                foreach (var month in r.Months) list.AddRange(await OneWayFaresAsync(r.From, r.To, month, direct, request.Currency, cancellationToken));
                fares[(r.From, r.To)] = list;
            }));

            // 3. Cheapest ways to make each direction on each day, then the cheapest combinations per departure day.
            IReadOnlyList<OneWayFare> Fares(string from, string to) => fares.TryGetValue((from, to), out var f) ? f : [];
            var offers = new List<FlightOffer>();
            var now = timeProvider.GetUtcNow();
            for (var i = 0; i < departureDays.Count; i++)
            {
                var outbound = Options(departureDays[i], Fares(origin, destination), outboundHubs.Select(h => (Fares(origin, h), Fares(h, destination))));
                var back = Options(returnDays[i], Fares(destination, origin), returnHubs.Select(h => (Fares(destination, h), Fares(h, origin))));
                foreach (var o in outbound)
                foreach (var b in back)
                    offers.Add(ToOffer(o, b, request, now));
            }

            return ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name,
                offers.DistinctBy(o => o.Signature).OrderBy(o => o.TotalPrice).Take(Math.Max(1, request.MaxResults)).ToList());
        }, cancellationToken);

    /// <summary>Up to MaxHubs cities that are cheap both to reach from <paramref name="from"/> and to leave for <paramref name="to"/>.</summary>
    private async Task<IReadOnlyList<string>> HubsAsync(string from, string to, string month, string currency, CancellationToken cancellationToken)
    {
        var leaving = await DiscoverAsync(origin: from, destination: null, month, currency, cancellationToken);
        var arriving = await DiscoverAsync(origin: null, destination: to, month, currency, cancellationToken);
        var fromCity = leaving.FirstOrDefault()?.FromCity;
        var toCity = arriving.FirstOrDefault()?.ToCity;
        bool Usable(string city) => city != from && city != to && city != fromCity && city != toCity;

        var cheapestLeaving = leaving.GroupBy(f => f.ToCity).ToDictionary(g => g.Key, g => g.Min(f => f.Price));
        var cheapestArriving = arriving.GroupBy(f => f.FromCity).ToDictionary(g => g.Key, g => g.Min(f => f.Price));
        var hubs = cheapestLeaving.Keys.Intersect(cheapestArriving.Keys).Where(Usable)
            .OrderBy(c => cheapestLeaving[c] + cheapestArriving[c])
            .ToList();
        // Top up with known hubs, preferring those that are already cheap from the origin or to the destination.
        hubs.AddRange(KnownHubs.Where(h => Usable(h) && !hubs.Contains(h))
            .OrderBy(h => cheapestLeaving.GetValueOrDefault(h, decimal.MaxValue) == decimal.MaxValue && cheapestArriving.GetValueOrDefault(h, decimal.MaxValue) == decimal.MaxValue ? 1 : 0)
            .ThenBy(h => Math.Min(cheapestLeaving.GetValueOrDefault(h, decimal.MaxValue), cheapestArriving.GetValueOrDefault(h, decimal.MaxValue))));
        return hubs.Take(MaxHubs).ToList();
    }

    private Task<IReadOnlyList<OneWayFare>> DiscoverAsync(string? origin, string? destination, string month, string currency, CancellationToken cancellationToken) =>
        CachedFaresAsync($"tp-discover|{origin}|{destination}|{month}|{currency}", origin, destination, month, null, currency, cancellationToken);

    private Task<IReadOnlyList<OneWayFare>> OneWayFaresAsync(string from, string to, string month, int? maxStops, string currency, CancellationToken cancellationToken) =>
        CachedFaresAsync($"tp-oneway|{from}|{to}|{month}|{maxStops}|{currency}", from, to, month, maxStops, currency, cancellationToken);

    private async Task<IReadOnlyList<OneWayFare>> CachedFaresAsync(string key, string? origin, string? destination, string month, int? maxStops,
        string currency, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(key, out IReadOnlyList<OneWayFare>? cached) && cached is not null) return cached;
        await Throttle.WaitAsync(cancellationToken);
        try
        {
            var result = await FetchAsync(origin, destination, month, null, maxStops, currency, MaxTicketsPerCall, cancellationToken);
            if (!result.Succeeded) return [];
            IReadOnlyList<OneWayFare> fares = result.Data!.Data.Select(ToFare).Where(f => f is not null).Select(f => f!).ToList();
            cache.Set(key, fares, FareCacheTtl);
            return fares;
        }
        finally
        {
            Throttle.Release();
        }
    }

    private static OneWayFare? ToFare(TravelpayoutsTicket t)
    {
        if (t.Price <= 0 || !DateTimeOffset.TryParse(t.DepartureAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var departure)) return null;
        var from = t.OriginAirport ?? t.Origin;
        var to = t.DestinationAirport ?? t.Destination;
        if (from is null || to is null) return null;
        var flightNumber = t.FlightNumber.ValueKind switch
        {
            JsonValueKind.Number => t.FlightNumber.GetRawText(),
            JsonValueKind.String => t.FlightNumber.GetString() ?? "",
            _ => ""
        };
        var minutes = t.DurationTo is > 0 ? t.DurationTo : null;
        return new OneWayFare(from, to, t.Origin ?? from, t.Destination ?? to, departure, minutes is null ? null : departure.AddMinutes(minutes.Value),
            t.Transfers, t.Airline, t.Price, t.Link, AviasalesRoute.Parse(t.Link).Via(0, from, to, t.Transfers) ?? [],
            $"{t.Airline}{flightNumber}|{t.DepartureAt}");
    }

    /// <summary>Cheapest ways (one ticket, or two through a hub) to travel on a day, cheapest first.</summary>
    public static IReadOnlyList<IReadOnlyList<OneWayFare>> Options(DateOnly day, IReadOnlyList<OneWayFare> direct,
        IEnumerable<(IReadOnlyList<OneWayFare> First, IReadOnlyList<OneWayFare> Second)> viaHubs)
    {
        var options = direct.Where(f => DateOnly.FromDateTime(f.Departure.DateTime) == day)
            .Select(f => (Tickets: (IReadOnlyList<OneWayFare>)[f], Price: f.Price)).ToList();
        foreach (var (first, second) in viaHubs)
        {
            foreach (var a in first.Where(f => f.Arrival.HasValue && DateOnly.FromDateTime(f.Departure.DateTime) == day))
            {
                var b = second
                    .Where(f => f.FromCity == a.ToCity)
                    .Where(f =>
                    {
                        var wait = f.Departure - a.Arrival!.Value;
                        return wait >= (f.From == a.To ? MinConnection : MinConnectionAirportChange) && wait <= MaxConnection;
                    })
                    .MinBy(f => f.Price);
                if (b is not null) options.Add(([a, b], a.Price + b.Price));
            }
        }
        return options.OrderBy(o => o.Price).Take(OptionsPerDay).Select(o => o.Tickets).ToList();
    }

    private FlightOffer ToOffer(IReadOnlyList<OneWayFare> outbound, IReadOnlyList<OneWayFare> back, FlightWindowRequest request, DateTimeOffset now)
    {
        // The destination's UTC offset is the one of the return's first departure, and vice versa.
        var outbound0 = outbound[0];
        var back0 = back[0];
        var tickets = Tickets(outbound, false, back0.Departure.Offset, request).Concat(Tickets(back, true, outbound0.Departure.Offset, request)).ToList();
        var selfTransfer = outbound.Count > 1 || back.Count > 1;
        var hubs = outbound.Skip(1).Concat(back.Skip(1)).Select(f => f.FromCity).Distinct().ToList();

        return new FlightOffer
        {
            Provider = Name,
            Signature = "combo|" + string.Join("+", outbound.Concat(back).Select(f => f.Signature)),
            TotalPrice = outbound.Concat(back).Sum(f => f.Price) * request.Adults,
            Currency = request.Currency.ToUpperInvariant(),
            Outbound = Itinerary(outbound, back0.Departure.Offset),
            Inbound = Itinerary(back, outbound0.Departure.Offset),
            BaggageNote = T.Get("note.travelpayoutsBaggage"),
            PriceExtrapolatedFromSinglePassenger = request.Adults > 1,
            ValidatingCarrier = outbound0.Airline,
            BookingUrl = _mapper.BuildLink(outbound0.Link),
            SourceNote = selfTransfer ? T.Get("flight.selfTransfer", string.Join(", ", hubs)) + " " + T.Get("note.selfTransfer") : T.Get("note.selfTransfer"),
            Verification = VerificationStatus.Cached,
            RetrievedAtUtc = now,
            Tickets = tickets
        };
    }

    private IEnumerable<FlightTicket> Tickets(IReadOnlyList<OneWayFare> fares, bool isReturn, TimeSpan endOffset, FlightWindowRequest request) =>
        fares.Select((f, i) => new FlightTicket(isReturn, f.From, f.To, f.Departure.DateTime,
            f.Arrival?.ToOffset(i + 1 < fares.Count ? fares[i + 1].Departure.Offset : endOffset).DateTime,
            f.Transfers, f.Airline, f.Price * request.Adults, request.Currency.ToUpperInvariant(), _mapper.BuildLink(f.Link)));

    private static FlightItinerary Itinerary(IReadOnlyList<OneWayFare> fares, TimeSpan endOffset)
    {
        var first = fares[0];
        var last = fares[^1];
        var via = new List<string>();
        for (var i = 0; i < fares.Count; i++)
        {
            via.AddRange(fares[i].Via);
            if (i + 1 < fares.Count)
            {
                via.Add(fares[i].To);
                if (fares[i + 1].From != fares[i].To) via.Add(fares[i + 1].From);
            }
        }
        var arrival = last.Arrival?.ToOffset(endOffset);
        return new FlightItinerary(first.From, last.To, first.Departure.DateTime, arrival?.DateTime,
            last.Arrival is { } end ? end - first.Departure : null,
            fares.Sum(f => f.Transfers) + fares.Count - 1, [], via);
    }

    private static List<DateOnly> Days(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1)) days.Add(d);
        return days;
    }

    private static IReadOnlyList<string> Months(IEnumerable<DateOnly> days, bool extraDay) =>
        days.SelectMany(d => extraDay ? [d, d.AddDays(1)] : new[] { d }).Select(Month).Distinct().Order().ToList();
}
