using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Catalog;

/// <summary>
/// Reads catalog/nearby.json, the OpenStreetMap snapshot written by scripts/fetch-nearby.mjs. Used as the fallback for
/// live Geoapify data and as the source of public-transport line descriptions (from → to), which Geoapify does not return.
/// A missing or invalid file is not fatal: the plan page then simply shows no nearby places.
/// The file is re-read when it changes on disk, so a refreshed snapshot needs no restart.
/// </summary>
public sealed class NearbyPlacesFile(IOptions<CatalogOptions> options, IHostEnvironment environment, ILogger<NearbyPlacesFile> logger)
{
    public const string DefaultAttribution = "© OpenStreetMap contributors";

    private readonly string _path = Path.IsPathRooted(options.Value.NearbyPath)
        ? options.Value.NearbyPath
        : Path.Combine(environment.ContentRootPath, options.Value.NearbyPath);
    private readonly Lock _gate = new();
    private Document _document = new();
    private DateTime? _loadedWriteTimeUtc;

    public NearbySnapshot? Find(string destinationCode)
    {
        var document = Current();
        var d = document.Destinations.FirstOrDefault(x => string.Equals(x.Code, destinationCode, StringComparison.OrdinalIgnoreCase));
        if (d is null) return null;
        return new NearbySnapshot(
            d.Code,
            d.RetrievedOn,
            document.Attribution ?? DefaultAttribution,
            d.Airports.Select(a => new NearbyAirport(a.Iata, a.Hotels.Where(Valid).Select(ToHotel).ToList(), a.TransitLines, [], a.TaxiRanks)).ToList(),
            new NearbyArea(d.Centre?.Hotels.Where(Valid).Select(ToHotel).ToList() ?? []),
            d.TaxiCompanies);
    }

    private static bool Valid(HotelEntry h) => !string.IsNullOrWhiteSpace(h.Name) && !string.IsNullOrWhiteSpace(h.OsmId);

    private static NearbyHotel ToHotel(HotelEntry h) =>
        new(h.OsmId!, h.Name!, h.NameEn, h.Kind ?? "hotel", h.Stars, h.Latitude, h.Longitude, h.Address, h.Phone, h.Website);

    private Document Current()
    {
        var writeTime = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : (DateTime?)null;
        var stamp = writeTime ?? DateTime.MinValue;
        lock (_gate)
        {
            if (_loadedWriteTimeUtc == stamp) return _document;
            _document = Load(writeTime);
            _loadedWriteTimeUtc = stamp;
            return _document;
        }
    }

    private Document Load(DateTime? writeTime)
    {
        try
        {
            if (writeTime is null)
            {
                logger.LogWarning("Nearby places snapshot not found at {Path}; plan pages will show no nearby hotels or transit lines", _path);
                return new Document();
            }
            return JsonSerializer.Deserialize<Document>(File.ReadAllText(_path), AppJson.Options) ?? new Document();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Keep the previous snapshot while the file is being rewritten or if it is invalid.
            logger.LogError(ex, "Nearby places snapshot at {Path} could not be read", _path);
            return _document;
        }
    }

    private sealed class Document
    {
        public string? Attribution { get; set; }
        public List<DestinationEntry> Destinations { get; set; } = [];
    }

    private sealed class DestinationEntry
    {
        public string Code { get; set; } = "";
        public DateOnly? RetrievedOn { get; set; }
        public List<AirportEntry> Airports { get; set; } = [];
        public AreaEntry? Centre { get; set; }
        public List<NearbyTaxiCompany> TaxiCompanies { get; set; } = [];
    }

    private sealed class AirportEntry
    {
        public string Iata { get; set; } = "";
        public List<HotelEntry> Hotels { get; set; } = [];
        public List<NearbyTransitLine> TransitLines { get; set; } = [];
        public List<NearbyTaxiRank> TaxiRanks { get; set; } = [];
    }

    private sealed class AreaEntry
    {
        public List<HotelEntry> Hotels { get; set; } = [];
    }

    private sealed class HotelEntry
    {
        public string? OsmId { get; set; }
        public string? Name { get; set; }
        public string? NameEn { get; set; }
        public string? Kind { get; set; }
        public int? Stars { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Website { get; set; }
    }
}
