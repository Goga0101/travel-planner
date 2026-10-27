namespace TravelPlanner.Application.Providers;

/// <summary>
/// Indicative hotel prices for a city and dates, per online travel agency (Booking.com, Agoda, ...).
/// They are estimates from a third-party price-comparison feed, not bookable offers: the UI labels them as such and
/// they are never added to a plan's total.
/// </summary>
public interface IHotelPriceSource
{
    /// <summary>Returns null when no price source is configured for the destination or the source failed.</summary>
    Task<HotelPriceResult?> GetAsync(HotelPriceQuery query, CancellationToken cancellationToken);
}

public sealed record HotelPriceQuery(string DestinationCode, DateOnly CheckIn, DateOnly CheckOut, int Adults, int Rooms, string Currency);

public sealed record HotelPriceResult(string Source, string Currency, DateTimeOffset RetrievedAtUtc, IReadOnlyList<HotelPriceOffer> Hotels);

public sealed record HotelPriceOffer(
    string Key,
    string Name,
    double? Rating,
    int? ReviewCount,
    double? Latitude,
    double? Longitude,
    decimal? TypicalNightlyMin,
    decimal? TypicalNightlyMax,
    IReadOnlyList<HotelRate> Rates,
    string? PageUrl = null);

/// <summary>Price per night for the requested rooms and guests, as listed by one agency.</summary>
public sealed record HotelRate(string Code, string Name, decimal Nightly);
