using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;
using TravelPlanner.Infrastructure.Catalog;

namespace TravelPlanner.Infrastructure.Providers.Geoapify;

public sealed class GeoapifyOptions
{
    public const string SectionName = "Providers:Geoapify";

    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.geoapify.com";
    /// <summary>Server-side key (never sent to the browser). Free tier: 3,000 requests/day.</summary>
    public string? ApiKey { get; set; }
    /// <summary>Places change rarely; one destination costs 1 + 3 per airport requests, then is served from memory.</summary>
    public int CacheHours { get; set; } = 24;
    public int AirportHotelRadiusMeters { get; set; } = 5000;
    public int CentreHotelRadiusMeters { get; set; } = 1200;
    public int StopRadiusMeters { get; set; } = 1500;
    public int TaxiRadiusMeters { get; set; } = 2500;
    public int MaxHotels { get; set; } = 12;

    public bool IsConfigured => Enabled && ProviderHttp.HasValue(ApiKey);
}

/// <summary>Thin client for the Geoapify Places API v2 (OpenStreetMap data). Returns raw GeoJSON features.</summary>
public sealed class GeoapifyPlacesClient(HttpClient http, IOptions<GeoapifyOptions> options)
{
    public const string ProviderName = "Geoapify";

    public async Task<IReadOnlyList<JsonElement>> SearchAsync(string categories, double lat, double lon, int radiusMeters, int limit, CancellationToken ct)
    {
        var c = CultureInfo.InvariantCulture;
        var url = $"v2/places?categories={categories}&filter=circle:{lon.ToString(c)},{lat.ToString(c)},{radiusMeters}" +
                  $"&bias=proximity:{lon.ToString(c)},{lat.ToString(c)}&limit={limit}&apiKey={Uri.EscapeDataString(options.Value.ApiKey!)}";
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return body.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array
            ? features.EnumerateArray().Select(f => f.Clone()).ToList()
            : [];
    }
}

/// <summary>Maps Geoapify GeoJSON features (OpenStreetMap tags in datasource.raw) to the application's nearby-place models.</summary>
public static class GeoapifyMapper
{
    public const string HotelCategories = "accommodation.hotel,accommodation.guest_house,accommodation.hostel,accommodation.motel,accommodation.apartment";
    public const string StopCategories = "public_transport.train,public_transport.subway,public_transport.light_rail,public_transport.tram,public_transport.bus";
    public const string TaxiCategories = "service.taxi";

    public static NearbyHotel? ToHotel(JsonElement feature)
    {
        if (!TryProperties(feature, out var p, out var raw)) return null;
        var name = Str(p, "name");
        if (name is null) return null;
        var kind = Categories(p).Select(c => c.StartsWith("accommodation.", StringComparison.Ordinal) ? c["accommodation.".Length..] : null)
            .FirstOrDefault(k => k is "hotel" or "guest_house" or "hostel" or "motel" or "apartment") ?? "hotel";
        var stars = int.TryParse(Str(raw, "stars")?.Split(' ', '.', 'S')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) && s is >= 1 and <= 5 ? s : (int?)null;
        var osmId = Str(raw, "osm_type") is { } type && raw.TryGetProperty("osm_id", out var id)
            ? $"{type switch { "n" => "node", "w" => "way", "r" => "relation", _ => type }}/{id.GetRawText()}"
            : "geoapify/" + Str(p, "place_id");
        var nameEn = p.TryGetProperty("name_international", out var intl) ? Str(intl, "en") : null;
        var phone = p.TryGetProperty("contact", out var contact) ? Str(contact, "phone") : Str(raw, "phone");
        return new NearbyHotel(osmId, name, nameEn, kind, stars, Num(p, "lat"), Num(p, "lon"),
            Str(p, "address_line2"), phone, Str(p, "website") ?? Str(raw, "website") ?? Str(raw, "contact:website"));
    }

    public static NearbyTransitStop? ToStop(JsonElement feature)
    {
        if (!TryProperties(feature, out var p, out var raw)) return null;
        var name = Str(p, "name");
        if (name is null) return null;
        var categories = Categories(p).ToList();
        var mode = new[] { "train", "subway", "light_rail", "tram", "bus" }.FirstOrDefault(m => categories.Contains("public_transport." + m)) ?? "bus";
        var lines = (Str(raw, "route_ref") ?? "")
            .Split([';', ',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        return new NearbyTransitStop(name, mode, lines, Num(p, "lat"), Num(p, "lon"));
    }

    public static NearbyTaxiRank? ToTaxiRank(JsonElement feature)
    {
        if (!TryProperties(feature, out var p, out var raw)) return null;
        var phone = p.TryGetProperty("contact", out var contact) ? Str(contact, "phone") : Str(raw, "phone");
        return new NearbyTaxiRank(Str(p, "name"), Str(raw, "operator"), phone, Num(p, "lat"), Num(p, "lon"));
    }

    private static bool TryProperties(JsonElement feature, out JsonElement properties, out JsonElement raw)
    {
        raw = default;
        properties = default;
        if (feature.ValueKind != JsonValueKind.Object || !feature.TryGetProperty("properties", out properties)) return false;
        if (!properties.TryGetProperty("lat", out _) || !properties.TryGetProperty("lon", out _)) return false;
        raw = properties.TryGetProperty("datasource", out var ds) && ds.TryGetProperty("raw", out var r) ? r : default;
        return true;
    }

    private static IEnumerable<string> Categories(JsonElement p) =>
        p.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!)
            : [];

    private static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        var s = v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > 160 ? s.Trim()[..160] : s.Trim();
    }

    private static double Num(JsonElement e, string name) => e.GetProperty(name).GetDouble();
}

/// <summary>
/// Nearby places for a destination: live from Geoapify (cached), merged with the OpenStreetMap snapshot file, which also
/// serves as the fallback when Geoapify is not configured or fails. Public-transport line descriptions and taxi
/// companies always come from the snapshot (Geoapify returns stops with line numbers, not the lines themselves).
/// </summary>
public sealed class NearbyPlacesService(
    NearbyPlacesFile file,
    IMemoryCache cache,
    IOptions<GeoapifyOptions> options,
    ILogger<NearbyPlacesService> logger,
    GeoapifyPlacesClient? geoapify = null) : INearbyPlacesSource
{
    public async Task<NearbySnapshot?> FindAsync(NearbyQuery query, CancellationToken cancellationToken)
    {
        var snapshot = file.Find(query.DestinationCode);
        if (geoapify is null || !options.Value.IsConfigured) return snapshot;

        var key = $"nearby|{query.DestinationCode}";
        if (!cache.TryGetValue(key, out NearbySnapshot? live) || live is null)
        {
            try
            {
                live = await LoadLiveAsync(query, cancellationToken);
                cache.Set(key, live, TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours)));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or Polly.Timeout.TimeoutRejectedException
                                           or Polly.CircuitBreaker.BrokenCircuitException && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Geoapify nearby places for {Destination} failed ({Error}); using the OpenStreetMap snapshot", query.DestinationCode, ex.GetType().Name);
                return snapshot;
            }
        }
        return Merge(live, snapshot);
    }

    private async Task<NearbySnapshot> LoadLiveAsync(NearbyQuery query, CancellationToken ct)
    {
        var o = options.Value;
        var centreTask = geoapify!.SearchAsync(GeoapifyMapper.HotelCategories, query.CentreLatitude, query.CentreLongitude, o.CentreHotelRadiusMeters, o.MaxHotels * 2, ct);
        var airportTasks = query.Airports.Select(async a =>
        {
            var hotels = geoapify.SearchAsync(GeoapifyMapper.HotelCategories, a.Latitude, a.Longitude, o.AirportHotelRadiusMeters, o.MaxHotels * 2, ct);
            var stops = geoapify.SearchAsync(GeoapifyMapper.StopCategories, a.Latitude, a.Longitude, o.StopRadiusMeters, 40, ct);
            var taxis = geoapify.SearchAsync(GeoapifyMapper.TaxiCategories, a.Latitude, a.Longitude, o.TaxiRadiusMeters, 6, ct);
            await Task.WhenAll(hotels, stops, taxis);
            return new NearbyAirport(
                a.Iata,
                BestHotels(hotels.Result, o.MaxHotels),
                [],
                Stops(stops.Result),
                taxis.Result.Select(GeoapifyMapper.ToTaxiRank).OfType<NearbyTaxiRank>().ToList());
        }).ToList();

        await Task.WhenAll(airportTasks.Cast<Task>().Append(centreTask));
        return new NearbySnapshot(
            query.DestinationCode,
            DateOnly.FromDateTime(DateTime.UtcNow),
            $"{NearbyPlacesFile.DefaultAttribution} · {GeoapifyPlacesClient.ProviderName}",
            airportTasks.Select(t => t.Result).ToList(),
            new NearbyArea(BestHotels(centreTask.Result, o.MaxHotels)),
            []);
    }

    /// <summary>Named, well-described hotels first (stars, phone or website), in the API's distance order otherwise.</summary>
    private static List<NearbyHotel> BestHotels(IReadOnlyList<JsonElement> features, int max) =>
        features.Select(GeoapifyMapper.ToHotel).OfType<NearbyHotel>()
            .DistinctBy(h => h.Name, StringComparer.OrdinalIgnoreCase)
            .Select((h, index) => (h, score: index + (h.Kind == "hotel" ? 0 : 6) - (h.Stars is null ? 0 : 3) - (h.Phone is null && h.Website is null ? 0 : 3)))
            .OrderBy(x => x.score)
            .Take(max)
            .Select(x => x.h)
            .ToList();

    /// <summary>Stops that list their lines, grouped by name (a terminal often has several platforms), rail before bus.</summary>
    private static List<NearbyTransitStop> Stops(IReadOnlyList<JsonElement> features)
    {
        var order = new[] { "train", "subway", "light_rail", "tram", "bus" };
        return features.Select(GeoapifyMapper.ToStop).OfType<NearbyTransitStop>()
            .Where(s => s.Lines.Count > 0)
            .GroupBy(s => (s.Name, s.Mode))
            .Select(g => g.First() with { Lines = g.SelectMany(s => s.Lines).Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList() })
            .OrderBy(s => Array.IndexOf(order, s.Mode))
            .Take(10)
            .ToList();
    }

    private static NearbySnapshot Merge(NearbySnapshot live, NearbySnapshot? snapshot)
    {
        if (snapshot is null) return live;
        var airports = live.Airports.Select(a =>
        {
            var fromFile = snapshot.Airports.FirstOrDefault(s => string.Equals(s.Iata, a.Iata, StringComparison.OrdinalIgnoreCase));
            return a with
            {
                Hotels = a.Hotels.Count > 0 ? a.Hotels : fromFile?.Hotels ?? [],
                TransitLines = fromFile?.TransitLines ?? [],
                TaxiRanks = a.TaxiRanks.Count > 0 ? a.TaxiRanks : fromFile?.TaxiRanks ?? [],
            };
        }).ToList();
        return live with
        {
            Airports = airports,
            Centre = live.Centre.Hotels.Count > 0 ? live.Centre : snapshot.Centre,
            TaxiCompanies = snapshot.TaxiCompanies,
        };
    }
}
