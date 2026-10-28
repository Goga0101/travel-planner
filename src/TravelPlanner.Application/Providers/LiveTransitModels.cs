namespace TravelPlanner.Application.Providers;

/// <summary>A place found by name anywhere in the world (address, street, stop, landmark, city). Area is "district, city, country".</summary>
public sealed record GeocodedPlace(string Name, string? Area, string? CountryCode, double Latitude, double Longitude, string Kind, string? TimeZone);

/// <summary>
/// One stretch of a vehicle's run between two consecutive stops, timed in UTC. The vehicle is on this stretch between
/// Departure and Arrival; its position follows the encoded polyline (Google format, precision 5). RealTime is true when
/// the times come from the operator's live feed rather than the timetable.
/// </summary>
public sealed record VehicleSegment(
    string TripId, string Mode, string? Line, string? Color, string FromStop, string ToStop,
    DateTimeOffset Departure, DateTimeOffset Arrival, bool RealTime, string Polyline);

public sealed record GeoBox(double MinLatitude, double MinLongitude, double MaxLatitude, double MaxLongitude);

/// <summary>Place search and vehicles currently running in an area (public-transport open data with live feeds where operators publish them).</summary>
public interface ILiveTransit
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<GeocodedPlace>> GeocodeAsync(string text, string language, double? nearLatitude, double? nearLongitude, CancellationToken cancellationToken);
    /// <summary>The address at a point with its country and time zone; null when unknown.</summary>
    Task<GeocodedPlace?> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken);
    Task<IReadOnlyList<VehicleSegment>> VehiclesAsync(GeoBox box, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

/// <summary>A taxi stand or taxi company near a point, with its phone when OpenStreetMap has one.</summary>
public sealed record LocalTaxi(string? Name, string? Operator, string? Phone, string? Website, double Latitude, double Longitude, bool IsCompany);

/// <summary>Taxi stands and companies around a point (live OpenStreetMap query).</summary>
public interface ILocalTaxiSource
{
    Task<IReadOnlyList<LocalTaxi>> FindAsync(double latitude, double longitude, int radiusMeters, CancellationToken cancellationToken);
}

/// <summary>Public-transport stops around a point with the lines that call there (OpenStreetMap route_ref), nearest first.</summary>
public interface ITransitStopSource
{
    bool IsConfigured { get; }
    Task<IReadOnlyList<NearbyTransitStop>> FindAsync(double latitude, double longitude, int radiusMeters, CancellationToken cancellationToken);
}

public sealed class NoTransitStopSource : ITransitStopSource
{
    public bool IsConfigured => false;
    public Task<IReadOnlyList<NearbyTransitStop>> FindAsync(double latitude, double longitude, int radiusMeters, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<NearbyTransitStop>>([]);
}

/// <summary>The bus (coach) station nearest to a point, anywhere in the world (OpenStreetMap), or null.</summary>
public interface IStationFinder
{
    Task<GeocodedPlace?> BusStationNearAsync(double latitude, double longitude, int radiusKm, CancellationToken cancellationToken);
}
