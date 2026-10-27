using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Providers;

public sealed record FlightSearchRequest(
    string OriginIata,
    string DestinationIata,
    DateOnly DepartureDate,
    DateOnly ReturnDate,
    int Adults,
    string Currency,
    int? MaxStops,
    bool RequireCheckedBag,
    int MaxResults);

/// <summary>Times are airport-local wall-clock times, as published by airlines.</summary>
public sealed record FlightSegment(
    string CarrierCode,
    string FlightNumber,
    string DepartureAirport,
    string ArrivalAirport,
    DateTime DepartureLocal,
    DateTime? ArrivalLocal,
    TimeSpan? Duration);

public sealed record FlightItinerary(
    string DepartureAirport,
    string ArrivalAirport,
    DateTime DepartureLocal,
    DateTime? ArrivalLocal,
    TimeSpan? Duration,
    int Stops,
    IReadOnlyList<FlightSegment> Segments,
    IReadOnlyList<string>? Via = null)
{
    /// <summary>Connection airports in order (IATA), when the provider reports them without full segment data.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> ViaAirports =>
        Via is { Count: > 0 } ? Via : Segments.Count > 1 ? Segments.Skip(1).Select(s => s.DepartureAirport).ToList() : [];
}

/// <summary>Application-owned, normalized round-trip offer.</summary>
public sealed record FlightOffer
{
    public required string Provider { get; init; }
    /// <summary>Stable signature (carriers, flight numbers, dates) used to re-find the offer on refresh.</summary>
    public required string Signature { get; init; }
    /// <summary>Total for all passengers.</summary>
    public required decimal TotalPrice { get; init; }
    public required string Currency { get; init; }
    public required FlightItinerary Outbound { get; init; }
    public required FlightItinerary Inbound { get; init; }
    public int? IncludedCheckedBags { get; init; }
    public string? BaggageNote { get; init; }
    /// <summary>True when the provider only gives a per-passenger price and the total was multiplied by the passenger count.</summary>
    public bool PriceExtrapolatedFromSinglePassenger { get; init; }
    public string? ValidatingCarrier { get; init; }
    public string? BookingUrl { get; init; }
    public string? SourceNote { get; init; }
    public required VerificationStatus Verification { get; init; }
    public required DateTimeOffset RetrievedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    /// <summary>Separately booked tickets when the offer combines one-way fares (empty for a single ticket).</summary>
    public IReadOnlyList<FlightTicket> Tickets { get; init; } = [];

    public TimeSpan? TotalDuration =>
        Outbound.Duration.HasValue && Inbound.Duration.HasValue ? Outbound.Duration + Inbound.Duration : null;

    public int MaxStops => Math.Max(Outbound.Stops, Inbound.Stops);
}

/// <summary>One separately booked ticket of a combined offer. Price is for all passengers.</summary>
public sealed record FlightTicket(
    bool IsReturn,
    string From,
    string To,
    DateTime DepartureLocal,
    DateTime? ArrivalLocal,
    int Stops,
    string? Carrier,
    decimal Price,
    string Currency,
    string? BookingUrl)
{
    /// <summary>True when a direction needs more than one ticket, i.e. the traveller changes planes on separate tickets.</summary>
    public static bool IsSelfTransfer(IReadOnlyList<FlightTicket> tickets) =>
        tickets.Count(t => !t.IsReturn) > 1 || tickets.Count(t => t.IsReturn) > 1;
}

public interface IFlightSearchProvider
{
    string Name { get; }
    bool IsDemo { get; }
    Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Round trips of exactly DurationDays (departure and return day included) departing on any day from EarliestDeparture
/// whose return is no later than LatestReturn.
/// </summary>
public sealed record FlightWindowRequest(
    string OriginIata,
    string DestinationIata,
    DateOnly EarliestDeparture,
    DateOnly LatestReturn,
    int DurationDays,
    int Adults,
    string Currency,
    int? MaxStops,
    bool RequireCheckedBag,
    int MaxResults);

/// <summary>
/// A provider that can price a whole date window in a few calls (e.g. cached fares grouped by month). The planner then
/// evaluates every departure day instead of a sample, which matters for long-haul routes where fares are sparse.
/// </summary>
/// <summary>
/// Combines separate one-way tickets for the window, directly or through a connecting city (a "self-transfer"), the way
/// low-cost travellers book. Each ticket keeps its own booking link.
/// </summary>
public interface ISelfTransferFlightProvider : IFlightSearchProvider
{
    Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchSelfTransferAsync(FlightWindowRequest request, CancellationToken cancellationToken);
}

public interface IFlightWindowSearchProvider : IFlightSearchProvider
{
    Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchWindowAsync(FlightWindowRequest request, CancellationToken cancellationToken);
}

/// <summary>Maps a provider-specific response into application-owned flight offers.</summary>
public interface IFlightOfferMapper<in TProviderResponse>
{
    IReadOnlyList<FlightOffer> Map(TProviderResponse response, FlightSearchRequest request, DateTimeOffset retrievedAtUtc);
}
