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
    IReadOnlyList<FlightSegment> Segments);

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

    public TimeSpan? TotalDuration =>
        Outbound.Duration.HasValue && Inbound.Duration.HasValue ? Outbound.Duration + Inbound.Duration : null;

    public int MaxStops => Math.Max(Outbound.Stops, Inbound.Stops);
}

public interface IFlightSearchProvider
{
    string Name { get; }
    bool IsDemo { get; }
    Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken);
}

/// <summary>Maps a provider-specific response into application-owned flight offers.</summary>
public interface IFlightOfferMapper<in TProviderResponse>
{
    IReadOnlyList<FlightOffer> Map(TProviderResponse response, FlightSearchRequest request, DateTimeOffset retrievedAtUtc);
}
