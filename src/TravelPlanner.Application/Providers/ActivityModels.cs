namespace TravelPlanner.Application.Providers;

/// <summary>What a traveller enjoys; each interest maps to place categories and planner slots.</summary>
public static class Interests
{
    public const string Culture = "culture";
    public const string Museums = "museums";
    public const string Food = "food";
    public const string Cafes = "cafes";
    public const string Bars = "bars";
    public const string Nightlife = "nightlife";
    public const string Nature = "nature";
    public const string Shopping = "shopping";
    public const string Shows = "shows";

    public static readonly IReadOnlyList<string> All = [Culture, Museums, Food, Cafes, Bars, Nightlife, Nature, Shopping, Shows];

    /// <summary>Interests that fill the morning and afternoon of a day.</summary>
    public static readonly IReadOnlyList<string> Daytime = [Culture, Museums, Nature, Shopping];

    public static string? Normalize(string? value) =>
        All.FirstOrDefault(i => string.Equals(i, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// A place to spend time (OpenStreetMap via Geoapify, or a ranked sight). Notable = described on Wikidata/Wikipedia,
/// the only quality signal open data offers (there are no ratings). Image and summary come from Wikimedia when known.
/// </summary>
public sealed record ActivityPlace(
    string Id,
    string Name,
    string Interest,
    string Kind,
    double Latitude,
    double Longitude,
    string? Address,
    string? OpeningHours,
    string? Website,
    string? Cuisine,
    string? ImageUrl,
    string? Summary,
    bool Notable);

public sealed record ActivityQuery(string DestinationCode, double Latitude, double Longitude, IReadOnlyList<string> Interests, string Language);

public interface IActivityPlaceSource
{
    Task<IReadOnlyList<ActivityPlace>> GetAsync(ActivityQuery query, CancellationToken cancellationToken);
}

/// <summary>A ticketed event (concert, show, match …). Prices are the provider's face-value range, when it publishes one.</summary>
public sealed record CityEvent(
    string Id,
    string Name,
    DateOnly Date,
    TimeOnly? Time,
    string? Venue,
    double? Latitude,
    double? Longitude,
    string? Segment,
    string? Genre,
    string? ImageUrl,
    decimal? PriceMin,
    decimal? PriceMax,
    string? Currency,
    string? Url,
    string? SubGenre = null);

public sealed record EventQuery(string City, string CountryCode, double Latitude, double Longitude, DateOnly From, DateOnly To, string Language);

public interface IEventSource
{
    string Name { get; }
    bool IsConfigured { get; }
    Task<IReadOnlyList<CityEvent>> GetAsync(EventQuery query, CancellationToken cancellationToken);
}
