using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Infrastructure.Catalog;

public sealed class CatalogOptions
{
    public const string SectionName = "Catalog";

    /// <summary>Path to the catalog JSON, relative to the content root unless absolute.</summary>
    public string Path { get; set; } = "catalog/destinations.json";
    public bool SeedOnStartup { get; set; } = true;
    /// <summary>OpenStreetMap snapshot of hotels, transit lines and taxis near each destination (scripts/fetch-nearby.mjs).</summary>
    public string NearbyPath { get; set; } = "catalog/nearby.json";
}

public sealed class CatalogDocument
{
    public List<CatalogAirport> DepartureAirports { get; set; } = [];
    public List<CatalogDestination> Destinations { get; set; } = [];

    public IEnumerable<CatalogAirport> AllAirports =>
        DepartureAirports.Concat(Destinations.SelectMany(d => d.Airports.Select(a => a with { City = d.City, CountryCode = d.CountryCode })));
}

public sealed record CatalogAirport
{
    public string Iata { get; init; } = null!;
    public string Name { get; init; } = null!;
    public string? City { get; init; }
    public string? CountryCode { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string? OfficialWebsiteUrl { get; init; }
    public bool IsDepartureOption { get; init; } = true;
    /// <summary>Airport name per language, e.g. { "ka": "..." }.</summary>
    public Dictionary<string, string> NameTranslations { get; init; } = new();
    /// <summary>City per language (departure airports only; destination airports use the destination's translations).</summary>
    public Dictionary<string, string> CityTranslations { get; init; } = new();
}

public sealed class CatalogDestination
{
    public string Code { get; set; } = null!;
    public string City { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string CountryCode { get; set; } = null!;
    public string Region { get; set; } = "Europe";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Popularity { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public long? BookingComCityId { get; set; }
    /// <summary>TripAdvisor geo id (e.g. "g187497"), used for indicative hotel prices (Xotelo). Optional.</summary>
    public string? TripAdvisorGeoKey { get; set; }
    /// <summary>Per-language city/country/description, e.g. { "ka": { "city": "ვენა", ... } }.</summary>
    public Dictionary<string, DestinationTranslation> Translations { get; set; } = new();
    public CatalogTaxiTariff? TaxiTariff { get; set; }
    public List<CatalogAirport> Airports { get; set; } = [];
    public List<CatalogTransportLink> TransportLinks { get; set; } = [];
}

public sealed class CatalogTaxiTariff
{
    public decimal BaseFare { get; set; }
    public decimal PerKm { get; set; }
    public string Currency { get; set; } = null!;
    public string Source { get; set; } = null!;
}

public sealed class CatalogTransportLink
{
    public TransportLinkKind Kind { get; set; }
    public string Name { get; set; } = null!;
    public string Url { get; set; } = null!;
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public bool IsVerified { get; set; }
    public DateOnly? VerifiedOn { get; set; }
    public Dictionary<string, string> NameTranslations { get; set; } = new();
}

/// <summary>Loads and caches the catalog file.</summary>
public sealed class CatalogFileReader(IOptions<CatalogOptions> options, IHostEnvironment environment)
{
    private readonly Lazy<CatalogDocument> _document = new(() => Load(options.Value, environment));

    public CatalogDocument Document => _document.Value;

    private static CatalogDocument Load(CatalogOptions options, IHostEnvironment environment)
    {
        var path = System.IO.Path.IsPathRooted(options.Path)
            ? options.Path
            : System.IO.Path.Combine(environment.ContentRootPath, options.Path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Destination catalog not found at '{path}'. Set Catalog:Path.");

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CatalogDocument>(json, AppJson.Options)
               ?? throw new InvalidOperationException("Destination catalog is empty.");
    }
}
