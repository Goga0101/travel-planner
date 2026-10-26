namespace TravelPlanner.Application.Providers;

/// <summary>
/// Places around a destination's airports and city centre: hotels, public transport serving the airport and taxis.
/// Live from Geoapify (OpenStreetMap data) when configured, otherwise from the dated OpenStreetMap snapshot file.
/// Never includes prices: OpenStreetMap has none.
/// </summary>
public interface INearbyPlacesSource
{
    Task<NearbySnapshot?> FindAsync(NearbyQuery query, CancellationToken cancellationToken);
}

public sealed record NearbyQuery(string DestinationCode, double CentreLatitude, double CentreLongitude, IReadOnlyList<NearbyAirportPoint> Airports);

public sealed record NearbyAirportPoint(string Iata, double Latitude, double Longitude);

public sealed record NearbySnapshot(
    string Code,
    DateOnly? RetrievedOn,
    string Attribution,
    IReadOnlyList<NearbyAirport> Airports,
    NearbyArea Centre,
    IReadOnlyList<NearbyTaxiCompany> TaxiCompanies);

public sealed record NearbyAirport(
    string Iata,
    IReadOnlyList<NearbyHotel> Hotels,
    IReadOnlyList<NearbyTransitLine> TransitLines,
    IReadOnlyList<NearbyTransitStop> TransitStops,
    IReadOnlyList<NearbyTaxiRank> TaxiRanks);

public sealed record NearbyArea(IReadOnlyList<NearbyHotel> Hotels);

public sealed record NearbyHotel(
    string OsmId,
    string Name,
    string? NameEn,
    string Kind,
    int? Stars,
    double Latitude,
    double Longitude,
    string? Address,
    string? Phone,
    string? Website);

public sealed record NearbyTransitLine(
    string Mode,
    string? Ref,
    string? Name,
    string? From,
    string? To,
    string? Operator,
    string? Network,
    string? Website);

/// <summary>A public-transport stop near the airport with the line numbers that serve it.</summary>
public sealed record NearbyTransitStop(string Name, string Mode, IReadOnlyList<string> Lines, double Latitude, double Longitude);

public sealed record NearbyTaxiRank(string? Name, string? Operator, string? Phone, double Latitude, double Longitude);

public sealed record NearbyTaxiCompany(string Name, string? Phone, string? Website);
