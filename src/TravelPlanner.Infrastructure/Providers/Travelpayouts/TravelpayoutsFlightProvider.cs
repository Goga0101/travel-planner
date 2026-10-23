using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public const string ProviderName = "Aviasales (Travelpayouts)";

    public IReadOnlyList<FlightOffer> Map(TravelpayoutsPricesResponse response, FlightSearchRequest request, DateTimeOffset retrievedAtUtc)
    {
        var currency = (response.Currency ?? request.Currency).ToUpperInvariant();
        var offers = new List<FlightOffer>();
        foreach (var t in response.Data)
        {
            if (t.Price <= 0 || !TryLocal(t.DepartureAt, out var dep) || !TryLocal(t.ReturnAt, out var ret)) continue;
            if (DateOnly.FromDateTime(dep) != request.DepartureDate || DateOnly.FromDateTime(ret) != request.ReturnDate) continue;

            var flightNumber = t.FlightNumber.ValueKind switch
            {
                JsonValueKind.Number => t.FlightNumber.GetRawText(),
                JsonValueKind.String => t.FlightNumber.GetString() ?? "",
                _ => ""
            };
            var from = t.OriginAirport ?? t.Origin ?? request.OriginIata;
            var to = t.DestinationAirport ?? t.Destination ?? request.DestinationIata;
            var airline = t.Airline ?? "";

            var outbound = new FlightItinerary(from, to, dep, null, Minutes(t.DurationTo), t.Transfers,
                t.Transfers == 0 && airline.Length > 0 ? [new FlightSegment(airline, $"{airline}{flightNumber}", from, to, dep, null, Minutes(t.DurationTo))] : []);
            var inbound = new FlightItinerary(to, from, ret, null, Minutes(t.DurationBack), t.ReturnTransfers, []);

            offers.Add(new FlightOffer
            {
                Provider = ProviderName,
                Signature = $"{airline}{flightNumber}|{t.DepartureAt}|{t.ReturnAt}",
                TotalPrice = t.Price * request.Adults,
                Currency = currency,
                Outbound = outbound,
                Inbound = inbound,
                IncludedCheckedBags = null,
                BaggageNote = T.Get("note.travelpayoutsBaggage"),
                PriceExtrapolatedFromSinglePassenger = request.Adults > 1,
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

    private string? BuildLink(string? relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || !relative.StartsWith('/')) return null;
        var url = options.LinkBaseUrl.TrimEnd('/') + relative;
        if (ProviderHttp.HasValue(options.Marker))
            url += (url.Contains('?') ? "&" : "?") + "marker=" + Uri.EscapeDataString(options.Marker!);
        return url;
    }

    private static TimeSpan? Minutes(int? m) => m is > 0 ? TimeSpan.FromMinutes(m.Value) : null;

    /// <summary>Departure times carry the airport's UTC offset; keep the local wall-clock time.</summary>
    private static bool TryLocal(string? value, out DateTime local)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
        {
            local = dto.DateTime;
            return true;
        }
        local = default;
        return false;
    }
}

public sealed class TravelpayoutsFlightProvider(
    HttpClient httpClient,
    IOptions<TravelpayoutsOptions> options,
    TimeProvider timeProvider,
    ILogger<TravelpayoutsFlightProvider> logger) : IFlightSearchProvider
{
    private readonly TravelpayoutsFlightOfferMapper _mapper = new(options.Value);

    public string Name => TravelpayoutsFlightOfferMapper.ProviderName;
    public bool IsDemo => false;

    public Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken) =>
        ProviderHttp.Guard(Name, async () =>
        {
            var query = new Dictionary<string, string>
            {
                ["origin"] = request.OriginIata,
                ["destination"] = request.DestinationIata,
                ["departure_at"] = ProviderHttp.Iso(request.DepartureDate),
                ["return_at"] = ProviderHttp.Iso(request.ReturnDate),
                ["one_way"] = "false",
                ["direct"] = request.MaxStops == 0 ? "true" : "false",
                ["currency"] = request.Currency.ToLowerInvariant(),
                ["sorting"] = "price",
                ["unique"] = "false",
                ["limit"] = Math.Clamp(request.MaxResults, 1, 100).ToString()
            };
            var url = "aviasales/v3/prices_for_dates?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Add("X-Access-Token", options.Value.ApiToken);
            using var response = await httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Travelpayouts search {Origin}->{Destination} returned {Status}", request.OriginIata, request.DestinationIata, (int)response.StatusCode);
                return ProviderResult<IReadOnlyList<FlightOffer>>.Failure(Name, ProviderHttp.Classify(response.StatusCode), $"Travelpayouts returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<TravelpayoutsPricesResponse>(cancellationToken);
            if (body is null || !body.Success)
                return ProviderResult<IReadOnlyList<FlightOffer>>.Failure(Name, ProviderErrorKind.InvalidResponse, "Travelpayouts returned an unsuccessful response.");

            return ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name, _mapper.Map(body, request, timeProvider.GetUtcNow()));
        }, cancellationToken);
}
