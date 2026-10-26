namespace TravelPlanner.Application.Providers;

/// <summary>
/// A city's best-known sights: OpenStreetMap places that have a Wikidata entry, ranked by how many Wikipedia
/// language editions describe them, with a short Wikipedia summary and the public transport stopping nearby.
/// </summary>
public interface IAttractionSource
{
    Task<IReadOnlyList<Attraction>> GetAsync(AttractionQuery query, CancellationToken cancellationToken);
}

/// <summary>Language is "en" or "ka"; names and summaries fall back to English when no Georgian article exists.</summary>
public sealed record AttractionQuery(string DestinationCode, double CentreLatitude, double CentreLongitude, string Language);

public sealed record Attraction(
    string WikidataId,
    string Name,
    string Kind,
    double Latitude,
    double Longitude,
    int WikipediaEditions,
    string? ShortDescription,
    string? Summary,
    string? SummaryLanguage,
    string? ImageUrl,
    string? WikipediaUrl,
    string? Website,
    string? OpeningHours,
    string? Fee,
    IReadOnlyList<AttractionStop> Stops);

/// <summary>A public-transport stop within walking distance of a sight, with the line numbers serving it.</summary>
public sealed record AttractionStop(string Name, string Mode, IReadOnlyList<string> Lines, int DistanceMeters);
