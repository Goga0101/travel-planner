namespace TravelPlanner.Application.Providers;

/// <summary>A hotel name (full or partial) typed by the visitor, looked up around the destination.</summary>
public sealed record HotelSearchQuery(string Text, double Latitude, double Longitude, int RadiusMeters);

public sealed record HotelSearchHit(string Id, string Name, string? Address, string Kind, double Latitude, double Longitude, string Source);

/// <summary>Finds accommodation by name near a point (OpenStreetMap geocoders); returns an empty list when nothing matches.</summary>
public interface IHotelSearch
{
    Task<IReadOnlyList<HotelSearchHit>> SearchAsync(HotelSearchQuery query, CancellationToken cancellationToken);
}
