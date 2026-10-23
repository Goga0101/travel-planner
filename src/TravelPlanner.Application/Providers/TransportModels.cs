using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Providers;

public enum TransferMode
{
    PublicTransport = 0,
    Taxi = 1,
    Drive = 2,
    RideHailing = 3,
    Information = 4
}

public sealed record TransportSearchRequest(
    Guid DestinationId,
    string AirportIata,
    string AirportName,
    double AirportLatitude,
    double AirportLongitude,
    double AccommodationLatitude,
    double AccommodationLongitude,
    string? AccommodationName,
    int Travelers,
    string Currency)
{
    public string CacheKey =>
        $"{AirportIata}|{AccommodationLatitude:F4},{AccommodationLongitude:F4}|{Travelers}|{Currency}";
}

/// <summary>
/// A way to get between airport and accommodation. Costs are estimates unless a provider explicitly
/// returns a confirmed quote; route data is never presented as a taxi quote.
/// </summary>
public sealed record TransferOption
{
    public required TransferMode Mode { get; init; }
    public required string Title { get; init; }
    public required string Provider { get; init; }
    public int? DistanceMeters { get; init; }
    public TimeSpan? Duration { get; init; }
    public decimal? EstimatedCost { get; init; }
    public string? CostCurrency { get; init; }
    /// <summary>True when the cost applies per traveller (public transport fares); false for per-vehicle costs.</summary>
    public bool CostIsPerPerson { get; init; }
    public VerificationStatus CostVerification { get; init; } = VerificationStatus.Unavailable;
    /// <summary>Verification of the route/contact information itself.</summary>
    public VerificationStatus InfoVerification { get; init; } = VerificationStatus.Estimate;
    public bool IsConfirmedQuote { get; init; }
    public string? Url { get; init; }
    public string? Phone { get; init; }
    public string? Notes { get; init; }
    public IReadOnlyList<string> Lines { get; init; } = [];

    public decimal? CostForTravelers(int travelers) =>
        EstimatedCost.HasValue ? (CostIsPerPerson ? EstimatedCost.Value * travelers : EstimatedCost.Value) : null;
}

public interface ITransportSearchProvider
{
    string Name { get; }
    bool IsDemo { get; }
    Task<ProviderResult<IReadOnlyList<TransferOption>>> SearchAsync(TransportSearchRequest request, CancellationToken cancellationToken);
}

public sealed record ExchangeRate(string From, string To, decimal Rate, string Source, DateTimeOffset AsOfUtc);

public interface IExchangeRateProvider
{
    /// <summary>Returns null when no valid rate is available. Never guesses.</summary>
    Task<ExchangeRate?> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken);
}
