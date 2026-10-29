using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Infrastructure.Providers.Travelpayouts;

public sealed class TravelpayoutsOptions
{
    public const string SectionName = "Providers:Travelpayouts";

    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "https://api.travelpayouts.com";
    public string? ApiToken { get; set; }
    /// <summary>Affiliate marker appended to Aviasales links (optional).</summary>
    public string? Marker { get; set; }
    public string LinkBaseUrl { get; set; } = "https://www.aviasales.com";

    public bool IsConfigured => Enabled && ProviderHttp.HasValue(ApiToken);
}

// Subset of GET /aviasales/v3/prices_for_dates

public sealed class TravelpayoutsPricesResponse
{
    [JsonPropertyName("success")] public bool Success { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("data")] public List<TravelpayoutsTicket> Data { get; set; } = [];
}

public sealed class TravelpayoutsTicket
{
    [JsonPropertyName("origin_airport")] public string? OriginAirport { get; set; }
    [JsonPropertyName("destination_airport")] public string? DestinationAirport { get; set; }
    [JsonPropertyName("origin")] public string? Origin { get; set; }
    [JsonPropertyName("destination")] public string? Destination { get; set; }
    [JsonPropertyName("price")] public decimal Price { get; set; }
    [JsonPropertyName("airline")] public string? Airline { get; set; }
    [JsonPropertyName("flight_number")] public JsonElement FlightNumber { get; set; }
    [JsonPropertyName("departure_at")] public string? DepartureAt { get; set; }
    [JsonPropertyName("return_at")] public string? ReturnAt { get; set; }
    [JsonPropertyName("transfers")] public int Transfers { get; set; }
    [JsonPropertyName("return_transfers")] public int ReturnTransfers { get; set; }
    [JsonPropertyName("duration_to")] public int? DurationTo { get; set; }
    [JsonPropertyName("duration_back")] public int? DurationBack { get; set; }
    [JsonPropertyName("link")] public string? Link { get; set; }
}

/// <summary>
/// Maps Aviasales Data API tickets. These are CACHED price indications (tickets found by Aviasales users recently),
/// priced per passenger, so they are labelled Cached and multiplied by the passenger count (and flagged as such).
/// </summary>
public sealed class TravelpayoutsFlightOfferMapper(TravelpayoutsOptions options) : IFlightOfferMapper<TravelpayoutsPricesResponse>
{
    public const string ProviderName = "Aviasales"; // what visitors know; Travelpayouts is only the affiliate network

    /// <summary>Tickets for exactly the requested departure and return dates.</summary>
    public IReadOnlyList<FlightOffer> Map(TravelpayoutsPricesResponse response, FlightSearchRequest request, DateTimeOffset retrievedAtUtc) =>
        MapAll(response, request.Adults, request.Currency, retrievedAtUtc)
            .Where(o => DateOnly.FromDateTime(o.Outbound.DepartureLocal) == request.DepartureDate &&
                        DateOnly.FromDateTime(o.Inbound.DepartureLocal) == request.ReturnDate)
            .ToList();

    /// <summary>Every priced round-trip ticket in the response, whatever its dates.</summary>
    public IReadOnlyList<FlightOffer> MapAll(TravelpayoutsPricesResponse response, int adults, string requestCurrency, DateTimeOffset retrievedAtUtc)
    {
        var currency = (response.Currency ?? requestCurrency).ToUpperInvariant();
        var offers = new List<FlightOffer>();
        foreach (var t in response.Data)
        {
            if (t.Price <= 0 || !TryParse(t.DepartureAt, out var dep) || !TryParse(t.ReturnAt, out var ret)) continue;

            var flightNumber = t.FlightNumber.ValueKind switch
            {
                JsonValueKind.Number => t.FlightNumber.GetRawText(),
                JsonValueKind.String => t.FlightNumber.GetString() ?? "",
                _ => ""
            };
            var from = t.OriginAirport ?? t.Origin ?? "";
            var to = t.DestinationAirport ?? t.Destination ?? "";
            var airline = t.Airline ?? "";
            var route = AviasalesRoute.Parse(t.Link);

            // Arrival = departure + flight time, in the other end's UTC offset (the return departs from the destination and vice versa).
            DateTime? arrivalOut = t.DurationTo is > 0 ? dep.AddMinutes(t.DurationTo.Value).ToOffset(ret.Offset).DateTime : null;
            DateTime? arrivalBack = t.DurationBack is > 0 ? ret.AddMinutes(t.DurationBack.Value).ToOffset(dep.Offset).DateTime : null;

            var outbound = new FlightItinerary(from, to, dep.DateTime, arrivalOut, Minutes(t.DurationTo), t.Transfers,
                t.Transfers == 0 && airline.Length > 0 ? [new FlightSegment(airline, $"{airline}{flightNumber}", from, to, dep.DateTime, arrivalOut, Minutes(t.DurationTo))] : [],
                route.Via(0, from, to, t.Transfers));
            var inbound = new FlightItinerary(to, from, ret.DateTime, arrivalBack, Minutes(t.DurationBack), t.ReturnTransfers, [],
                route.Via(1, to, from, t.ReturnTransfers));

            offers.Add(new FlightOffer
            {
                Provider = ProviderName,
                Signature = $"{airline}{flightNumber}|{t.DepartureAt}|{t.ReturnAt}",
                TotalPrice = t.Price * adults,
                Currency = currency,
                Outbound = outbound,
                Inbound = inbound,
                IncludedCheckedBags = null,
                BaggageNote = T.Get("note.travelpayoutsBaggage"),
                PriceExtrapolatedFromSinglePassenger = adults > 1,
                ValidatingCarrier = airline.Length > 0 ? airline : null,
                BookingUrl = BuildLink(t.Link),
                SourceNote = T.Get("note.travelpayouts"),
                Verification = VerificationStatus.Cached,
                RetrievedAtUtc = retrievedAtUtc,
                ExpiresAtUtc = null
            });
        }
        return offers;
    }

    public string? BuildLink(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || !relative.StartsWith('/')) return null;
        var url = options.LinkBaseUrl.TrimEnd('/') + relative;
        if (ProviderHttp.HasValue(options.Marker))
            url += (url.Contains('?') ? "&" : "?") + "marker=" + Uri.EscapeDataString(options.Marker!);
        return url;
    }

    private static TimeSpan? Minutes(int? m) => m is > 0 ? TimeSpan.FromMinutes(m.Value) : null;

    /// <summary>Times carry the airport's UTC offset; DateTime of the result is the local wall-clock time.</summary>
    private static bool TryParse(string? value, out DateTimeOffset dto) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out dto);
}

/// <summary>
/// The airports a ticket passes through, read from the "t" parameter of the Aviasales search link
/// (e.g. t=CA17985846001798790700003015TBSURCTFUPEKSYD1800354600...SYDPEKISTSAWTBS_hash). The parameter is not a
/// documented API, so anything unexpected yields no route rather than a guess.
/// </summary>
public sealed class AviasalesRoute
{
    // Each direction: departure and arrival Unix times, a 6-digit number, then the airport codes in order.
    private static readonly Regex LegPattern = new(@"\G(\d{10})(\d{10})(\d{6})((?:[A-Z]{3}){2,})", RegexOptions.Compiled);
    private static readonly AviasalesRoute Empty = new([]);

    private AviasalesRoute(IReadOnlyList<IReadOnlyList<string>> legs) => Legs = legs;

    /// <summary>Airport codes of each direction, origin and destination included.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Legs { get; }

    public static AviasalesRoute Parse(string? link)
    {
        if (string.IsNullOrEmpty(link)) return Empty;
        var start = link.IndexOf("t=", StringComparison.Ordinal);
        if (start <= 0 || link[start - 1] is not ('?' or '&')) return Empty;
        var end = link.IndexOfAny(['&', '_'], start);
        var value = link[(start + 2)..(end < 0 ? link.Length : end)];
        if (value.Length < 2) return Empty;

        var legs = new List<IReadOnlyList<string>>();
        var body = value[2..]; // 2-character prefix
        var position = 0;
        while (position < body.Length)
        {
            var match = LegPattern.Match(body, position);
            if (!match.Success) return Empty;
            var codes = match.Groups[4].Value;
            legs.Add(Enumerable.Range(0, codes.Length / 3).Select(i => codes.Substring(i * 3, 3)).ToList());
            position += match.Length;
        }
        return new AviasalesRoute(legs);
    }

    /// <summary>Connection airports of one direction, or null when the route does not match the ticket.</summary>
    public IReadOnlyList<string>? Via(int leg, string from, string to, int transfers)
    {
        if (leg >= Legs.Count) return null;
        var codes = Legs[leg];
        if (!codes[0].Equals(from, StringComparison.OrdinalIgnoreCase) || !codes[^1].Equals(to, StringComparison.OrdinalIgnoreCase)) return null;
        var via = codes.Skip(1).Take(codes.Count - 2).ToList();
        // A change of airport within a city (e.g. IST to SAW) adds a code without adding a transfer.
        return via.Count >= transfers ? via : null;
    }
}

public sealed partial class TravelpayoutsFlightProvider(
    HttpClient httpClient,
    IOptions<TravelpayoutsOptions> options,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<TravelpayoutsFlightProvider> logger) : IFlightWindowSearchProvider, ISelfTransferFlightProvider
{
    /// <summary>The Data API returns at most this many tickets per call.</summary>
    private const int MaxTicketsPerCall = 1000;

    private readonly TravelpayoutsFlightOfferMapper _mapper = new(options.Value);

    public string Name => TravelpayoutsFlightOfferMapper.ProviderName;
    public bool IsDemo => false;

    public Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken) =>
        ProviderHttp.Guard(Name, async () =>
        {
            var result = await FetchAsync(request.OriginIata, request.DestinationIata, ProviderHttp.Iso(request.DepartureDate),
                ProviderHttp.Iso(request.ReturnDate), request.MaxStops, request.Currency, request.MaxResults, cancellationToken);
            return result.Succeeded
                ? ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name, _mapper.Map(result.Data!, request, timeProvider.GetUtcNow()))
                : ProviderResult<IReadOnlyList<FlightOffer>>.Failure(Name, result.Error, result.ErrorMessage ?? "");
        }, cancellationToken);

    /// <summary>
    /// One call per (departure month, return month) pair in the window: the API accepts months as dates and returns the
    /// cheapest cached tickets of those months, which are then filtered to the exact trip length.
    /// </summary>
    public Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchWindowAsync(FlightWindowRequest request, CancellationToken cancellationToken) =>
        ProviderHttp.Guard(Name, async () =>
        {
            var lastDeparture = request.LatestReturn.AddDays(-(request.DurationDays - 1));
            var months = new SortedSet<(string Departure, string Return)>();
            for (var d = request.EarliestDeparture; d <= lastDeparture; d = d.AddDays(1))
                months.Add((Month(d), Month(d.AddDays(request.DurationDays - 1))));

            var offers = new List<FlightOffer>();
            ProviderResult<TravelpayoutsPricesResponse>? failure = null;
            var now = timeProvider.GetUtcNow();
            foreach (var (departureMonth, returnMonth) in months)
            {
                var result = await FetchAsync(request.OriginIata, request.DestinationIata, departureMonth, returnMonth, request.MaxStops,
                    request.Currency, MaxTicketsPerCall, cancellationToken);
                if (!result.Succeeded)
                {
                    failure = result;
                    continue;
                }
                offers.AddRange(_mapper.MapAll(result.Data!, request.Adults, request.Currency, now).Where(o =>
                {
                    var departure = DateOnly.FromDateTime(o.Outbound.DepartureLocal);
                    return departure >= request.EarliestDeparture && departure <= lastDeparture &&
                           DateOnly.FromDateTime(o.Inbound.DepartureLocal) == departure.AddDays(request.DurationDays - 1);
                }));
            }

            if (offers.Count == 0 && failure is not null)
                return ProviderResult<IReadOnlyList<FlightOffer>>.Failure(Name, failure.Error, failure.ErrorMessage ?? "");
            return ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name,
                offers.DistinctBy(o => o.Signature).OrderBy(o => o.TotalPrice).Take(Math.Max(1, request.MaxResults)).ToList());
        }, cancellationToken);

    /// <summary>
    /// Round trip when returnAt is set, otherwise one way; origin or destination may be omitted ("from/to anywhere"), and so
    /// may the departure date (the API then returns its cheapest fares of any date).
    /// </summary>
    private async Task<ProviderResult<TravelpayoutsPricesResponse>> FetchAsync(string? origin, string? destination, string? departureAt, string? returnAt,
        int? maxStops, string currency, int limit, CancellationToken cancellationToken)
    {
        var query = new Dictionary<string, string>
        {
            ["one_way"] = returnAt is null ? "true" : "false",
            ["direct"] = maxStops == 0 ? "true" : "false",
            ["currency"] = currency.ToLowerInvariant(),
            ["sorting"] = "price",
            ["unique"] = "false",
            ["limit"] = Math.Clamp(limit, 1, MaxTicketsPerCall).ToString(CultureInfo.InvariantCulture)
        };
        if (departureAt is not null) query["departure_at"] = departureAt;
        if (origin is not null) query["origin"] = origin;
        if (destination is not null) query["destination"] = destination;
        if (returnAt is not null) query["return_at"] = returnAt;
        var url = "aviasales/v3/prices_for_dates?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
        using var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Add("X-Access-Token", options.Value.ApiToken);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Travelpayouts search {Origin}->{Destination} returned {Status}", origin, destination, (int)response.StatusCode);
            return ProviderResult<TravelpayoutsPricesResponse>.Failure(Name, ProviderHttp.Classify(response.StatusCode), $"Travelpayouts returned HTTP {(int)response.StatusCode}.");
        }

        var body = await response.Content.ReadFromJsonAsync<TravelpayoutsPricesResponse>(cancellationToken);
        return body is null || !body.Success
            ? ProviderResult<TravelpayoutsPricesResponse>.Failure(Name, ProviderErrorKind.InvalidResponse, "Travelpayouts returned an unsuccessful response.")
            : ProviderResult<TravelpayoutsPricesResponse>.Success(Name, body);
    }

    private static string Month(DateOnly date) => date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
