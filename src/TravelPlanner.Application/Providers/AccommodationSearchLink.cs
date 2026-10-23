namespace TravelPlanner.Application.Providers;

/// <summary>A link to a provider's public search results page (no price data is fetched or shown).</summary>
public sealed record AccommodationSearchLink(string Provider, string Url);

/// <summary>
/// Builds a pre-filled accommodation search link (city, dates, guests) for plans without priced accommodation.
/// This needs no API access; affiliate tracking is optional configuration.
/// </summary>
public interface IAccommodationSearchLinkBuilder
{
    /// <summary>Returns null when link building is disabled.</summary>
    AccommodationSearchLink? Build(string city, string country, DateOnly checkIn, DateOnly checkOut, int adults, int rooms);
}
