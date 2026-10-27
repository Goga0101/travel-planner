using TravelPlanner.Domain.Common;

namespace TravelPlanner.Domain.Entities;

/// <summary>A city in the configurable destination catalog.</summary>
public class Destination : Entity
{
    private Destination() { }

    public Destination(string code, string city, string countryCode, string country, double latitude, double longitude)
    {
        Code = code;
        City = city;
        CountryCode = countryCode;
        Country = country;
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Stable catalog key, e.g. "barcelona".</summary>
    public string Code { get; private set; } = null!;
    public string City { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string Region { get; set; } = "Europe";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? Description { get; set; }
    /// <summary>Higher = considered earlier in automatic mode when request limits apply.</summary>
    public int Popularity { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>False for cities created on demand from the world airport list (not curated, never deactivated by the seeder).</summary>
    public bool IsCatalog { get; set; } = true;
    /// <summary>Booking.com Demand API city id. Verify via the /common/locations endpoints before production use.</summary>
    public long? BookingComCityId { get; set; }

    /// <summary>Optional curated taxi tariff, used only for clearly labelled estimates.</summary>
    public decimal? TaxiBaseFare { get; set; }
    public decimal? TaxiPerKm { get; set; }
    public string? TaxiTariffCurrency { get; set; }
    public string? TaxiTariffSource { get; set; }

    /// <summary>Per-language city/country/description, keyed by two-letter language code (e.g. "ka").</summary>
    public Dictionary<string, DestinationTranslation> Translations { get; set; } = new();

    public string CityIn(string language) =>
        Translations.TryGetValue(language, out var t) && !string.IsNullOrWhiteSpace(t.City) ? t.City : City;

    public string CountryIn(string language) =>
        Translations.TryGetValue(language, out var t) && !string.IsNullOrWhiteSpace(t.Country) ? t.Country : Country;

    public string? DescriptionIn(string language) =>
        Translations.TryGetValue(language, out var t) && !string.IsNullOrWhiteSpace(t.Description) ? t.Description : Description;

    public List<Airport> Airports { get; private set; } = [];
    public List<DestinationTransportLink> TransportLinks { get; private set; } = [];

    public bool HasTaxiTariff => TaxiBaseFare.HasValue && TaxiPerKm.HasValue && !string.IsNullOrWhiteSpace(TaxiTariffCurrency);
}
