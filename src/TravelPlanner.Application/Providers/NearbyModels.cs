namespace TravelPlanner.Application.Providers;

/// <summary>
/// Places around a destination's airports and city centre: hotels, public-transport lines serving the airport and taxis.
/// Comes from a dated OpenStreetMap snapshot (no prices; OSM has none).
/// </summary>
public interface INearbyPlacesSource
{
    string Attribution { get; }
    NearbySnapshot? Find(string destinationCode);
}

public sealed record NearbySnapshot(
    string Code,
    DateOnly? RetrievedOn,
    IReadOnlyList<NearbyAirport> Airports,
    NearbyArea Centre,
    IReadOnlyList<NearbyTaxiCompany> TaxiCompanies);

public sealed record NearbyAirport(
    string Iata,
    IReadOnlyList<NearbyHotel> Hotels,
    IReadOnlyList<NearbyTransitLine> TransitLines,
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

public sealed record NearbyTaxiRank(string? Name, string? Operator, string? Phone, double Latitude, double Longitude);

public sealed record NearbyTaxiCompany(string Name, string? Phone, string? Website);
