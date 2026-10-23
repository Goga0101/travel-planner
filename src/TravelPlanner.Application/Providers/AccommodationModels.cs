using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Providers;

public sealed record AccommodationSearchRequest(
    Guid DestinationId,
    string City,
    string CountryCode,
    long? BookingComCityId,
    double Latitude,
    double Longitude,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Adults,
    int Rooms,
    string Currency,
    int? MinStars,
    int MaxResults)
{
    public int Nights => CheckOut.DayNumber - CheckIn.DayNumber;
    public string CacheKey => $"{DestinationId:N}|{CheckIn:yyyyMMdd}|{CheckOut:yyyyMMdd}|{Adults}|{Rooms}|{Currency}|{MinStars}";
}

/// <param name="IncludedInPrice">True when the amount is already part of <see cref="AccommodationOffer.TotalPrice"/>.</param>
/// <param name="PayableAtProperty">True when the guest pays it on site rather than at booking.</param>
public sealed record MandatoryFee(string Name, decimal Amount, string Currency, bool IncludedInPrice, bool PayableAtProperty);

public sealed record AccommodationOffer
{
    public required string Provider { get; init; }
    public required string PropertyId { get; init; }
    public required string Name { get; init; }
    public string? Address { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public decimal? Stars { get; init; }
    /// <summary>Guest review score on a 0-10 scale.</summary>
    public decimal? ReviewScore { get; init; }
    public string? Category { get; init; }
    /// <summary>Total for the whole stay and all guests.</summary>
    public required decimal TotalPrice { get; init; }
    public required string Currency { get; init; }
    public IReadOnlyList<MandatoryFee> Fees { get; init; } = [];
    public string? CancellationPolicy { get; init; }
    public DateTimeOffset? FreeCancellationUntil { get; init; }
    public string? MealPlan { get; init; }
    public string? BookingUrl { get; init; }
    public required VerificationStatus Verification { get; init; }
    public required DateTimeOffset RetrievedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    public bool HasCoordinates => Latitude.HasValue && Longitude.HasValue;
}

public interface IAccommodationSearchProvider
{
    string Name { get; }
    bool IsDemo { get; }
    Task<ProviderResult<IReadOnlyList<AccommodationOffer>>> SearchAsync(AccommodationSearchRequest request, CancellationToken cancellationToken);
}
