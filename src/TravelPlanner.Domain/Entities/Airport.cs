using TravelPlanner.Domain.Common;

namespace TravelPlanner.Domain.Entities;

public class Airport : Entity
{
    private Airport() { }

    public Airport(string iataCode, string name, string city, string countryCode, double latitude, double longitude)
    {
        IataCode = iataCode.ToUpperInvariant();
        Name = name;
        City = city;
        CountryCode = countryCode;
        Latitude = latitude;
        Longitude = longitude;
    }

    public string IataCode { get; private set; } = null!;
    public string Name { get; set; } = null!;
    public string City { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    /// <summary>Offered in the departure selector.</summary>
    public bool IsDepartureOption { get; set; }
    public string? OfficialWebsiteUrl { get; set; }
    /// <summary>Airport name/city per language code (e.g. "ka").</summary>
    public Dictionary<string, AirportTranslation> Translations { get; set; } = new();

    public string NameIn(string language) =>
        Translations.TryGetValue(language, out var t) && !string.IsNullOrWhiteSpace(t.Name) ? t.Name : Name;

    public string CityIn(string language) =>
        Translations.TryGetValue(language, out var t) && !string.IsNullOrWhiteSpace(t.City) ? t.City : City;

    public Guid? DestinationId { get; set; }
    public Destination? Destination { get; set; }
}
