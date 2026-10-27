namespace TravelPlanner.Application.Providers;

/// <summary>A city with at least one flightable airport (world airport snapshot). Coordinates are the city centre.</summary>
public sealed record WorldCity(
    string CityCode,
    string City,
    string CountryCode,
    string Country,
    string Continent,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> AirportCodes,
    string? TimeZone = null);

/// <summary>Cities and airports worldwide, beyond the curated destination catalog.</summary>
public interface IWorldPlaces
{
    /// <summary>The city for an IATA city code ("LON") or airport code ("LHR"); null when unknown.</summary>
    WorldCity? FindCity(string cityOrAirportCode);

    /// <summary>Cities matching a name, country or IATA code, best matches first.</summary>
    IReadOnlyList<WorldCity> SearchCities(string term, int max);
}

/// <summary>Approximate location of a visitor; Iata is the nearest city's IATA code when the service knows it.</summary>
public sealed record VisitorLocation(string? Iata, string? City, double Latitude, double Longitude);

public interface IVisitorLocator
{
    /// <summary>Locates an IP address (null = the caller's public address as seen by the service). Null when unknown.</summary>
    Task<VisitorLocation?> LocateAsync(string? ipAddress, CancellationToken cancellationToken);
}
