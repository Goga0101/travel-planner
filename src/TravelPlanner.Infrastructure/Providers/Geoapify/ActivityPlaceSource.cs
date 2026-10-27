using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Geoapify;

/// <summary>
/// Places for the day planner: the ranked sights (with Wikipedia photo and summary) for culture, and Geoapify
/// (OpenStreetMap) categories for everything else. Places that have a Wikidata entry get its main image (P18) from
/// Wikimedia Commons and count as notable. Cached per destination and interest for a day.
/// </summary>
public sealed class ActivityPlaceSource(
    GeoapifyPlacesClient geoapify,
    IAttractionSource attractions,
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    IOptions<GeoapifyOptions> options,
    ILogger<ActivityPlaceSource> logger) : IActivityPlaceSource
{
    private const int RadiusMeters = 3500;
    private const int Limit = 80;

    private static readonly Dictionary<string, string> Categories = new()
    {
        [Interests.Museums] = "entertainment.museum",
        [Interests.Food] = "catering.restaurant",
        [Interests.Cafes] = "catering.cafe",
        [Interests.Bars] = "catering.bar,catering.pub,catering.biergarten",
        [Interests.Nightlife] = "adult.nightclub",
        [Interests.Nature] = "leisure.park,beach",
        [Interests.Shopping] = "commercial.shopping_mall,commercial.marketplace,commercial.department_store",
        [Interests.Shows] = "entertainment.culture.theatre,entertainment.culture.arts_centre",
    };

    public async Task<IReadOnlyList<ActivityPlace>> GetAsync(ActivityQuery query, CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured) return [];
        var lang = query.Language == "ka" ? "ka" : "en";
        var lists = await Task.WhenAll(query.Interests.Distinct().Select(i => ForInterestAsync(query, i, lang, cancellationToken)));
        return lists.SelectMany(l => l).ToList();
    }

    private async Task<IReadOnlyList<ActivityPlace>> ForInterestAsync(ActivityQuery query, string interest, string lang, CancellationToken ct)
    {
        try
        {
            return await cache.GetOrCreateAsync($"activities|{query.DestinationCode}|{interest}|{lang}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours));
                return interest == Interests.Culture ? await SightsAsync(query, lang, ct) : await PlacesAsync(query, interest, lang, ct);
            }) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Activities {Interest} for {Destination} failed ({Error})", interest, query.DestinationCode, ex.GetType().Name);
            return [];
        }
    }

    private async Task<IReadOnlyList<ActivityPlace>> SightsAsync(ActivityQuery query, string lang, CancellationToken ct)
    {
        var sights = await attractions.GetAsync(new AttractionQuery(query.DestinationCode, query.Latitude, query.Longitude, lang), ct);
        return sights.Select(a => new ActivityPlace("wd:" + a.WikidataId, a.Name, Interests.Culture, a.Kind, a.Latitude, a.Longitude, null,
            a.OpeningHours, a.Website, null, a.ImageUrl, a.Summary ?? a.ShortDescription, Notable: true)).ToList();
    }

    private async Task<IReadOnlyList<ActivityPlace>> PlacesAsync(ActivityQuery query, string interest, string lang, CancellationToken ct)
    {
        var features = await geoapify.SearchAsync(Categories[interest], query.Latitude, query.Longitude,
            interest == Interests.Shopping ? RadiusMeters * 2 : RadiusMeters, Limit, ct);
        var places = features.Select(f => ToPlace(f, interest, lang)).OfType<ActivityPlace>().DistinctBy(p => p.Id).ToList();

        var withWikidata = places.Where(p => p.Notable).Select(p => p.Id[3..]).Distinct().ToList();
        if (withWikidata.Count == 0) return places;
        var images = await WikidataImagesAsync(withWikidata, ct);
        return places.Select(p => p.Notable && images.TryGetValue(p.Id[3..], out var image) ? p with { ImageUrl = image } : p).ToList();
    }

    /// <summary>A named OSM place; the id is "wd:Q…" when it has a Wikidata entry, otherwise "osm:…".</summary>
    public static ActivityPlace? ToPlace(JsonElement f, string interest, string lang)
    {
        if (!f.TryGetProperty("properties", out var p) || !p.TryGetProperty("lat", out var lat) || !p.TryGetProperty("lon", out var lon)) return null;
        var raw = p.TryGetProperty("datasource", out var ds) && ds.TryGetProperty("raw", out var r) ? r : default;
        // Local names can be in any script (e.g. Greek): prefer the visitor's language, then English, then the local name.
        var name = (lang == "ka" ? Str(raw, "name:ka") : null) ?? Str(raw, "name:en") ?? Str(p, "name_international") ?? Str(p, "name");
        if (name is null) return null;
        var wikidata = Str(raw, "wikidata") is { } q && q.StartsWith('Q') ? q : null;
        var osmId = Str(raw, "osm_id") ?? (raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("osm_id", out var oid) ? oid.GetRawText() : null)
                    ?? Str(p, "place_id");
        if (wikidata is null && osmId is null) return null;
        var categories = p.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : [];
        var street = Str(p, "street");
        var house = Str(p, "housenumber");
        var address = street is null ? Str(p, "address_line2") : house is null ? street : $"{street} {house}";
        var cuisine = Str(raw, "cuisine")?.Replace(';', ',').Replace('_', ' ');
        return new ActivityPlace(
            wikidata is not null ? "wd:" + wikidata : "osm:" + osmId,
            name,
            interest,
            Kind(interest, categories),
            lat.GetDouble(),
            lon.GetDouble(),
            address,
            Str(raw, "opening_hours") is { Length: <= 80 } hours ? hours : null,
            Str(p, "website") ?? Str(raw, "website") ?? Str(raw, "contact:website"),
            cuisine,
            null,
            null,
            Notable: wikidata is not null);
    }

    private static string Kind(string interest, IReadOnlyCollection<string> categories)
    {
        if (categories.Contains("beach")) return "beach";
        if (categories.Contains("catering.pub")) return "pub";
        if (categories.Contains("commercial.marketplace")) return "market";
        if (categories.Contains("entertainment.culture.theatre")) return "theatre";
        return interest switch
        {
            Interests.Food => "restaurant",
            Interests.Cafes => "cafe",
            Interests.Bars => "bar",
            Interests.Nightlife => "club",
            Interests.Museums => "museum",
            Interests.Nature => "park",
            Interests.Shopping => "shopping",
            _ => interest
        };
    }

    /// <summary>Main image (P18) of Wikidata entities as a 480 px Commons thumbnail.</summary>
    private async Task<Dictionary<string, string>> WikidataImagesAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(AttractionSource.WikimediaClient);
        var result = new Dictionary<string, string>();
        foreach (var batch in ids.Chunk(50))
        {
            var body = await http.GetFromJsonAsync<JsonElement>(
                "https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=claims&ids=" + string.Join('|', batch), ct);
            if (!body.TryGetProperty("entities", out var entities)) continue;
            foreach (var e in entities.EnumerateObject())
            {
                if (!e.Value.TryGetProperty("claims", out var claims) || !claims.TryGetProperty("P18", out var p18) || p18.GetArrayLength() == 0) continue;
                var file = p18[0].TryGetProperty("mainsnak", out var snak) && snak.TryGetProperty("datavalue", out var dv) && dv.TryGetProperty("value", out var v)
                    ? v.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(file))
                    result[e.Name] = "https://commons.wikimedia.org/wiki/Special:FilePath/" + Uri.EscapeDataString(file.Replace(' ', '_')) + "?width=480";
            }
        }
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}

public sealed class NoActivityPlaceSource : IActivityPlaceSource
{
    public Task<IReadOnlyList<ActivityPlace>> GetAsync(ActivityQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ActivityPlace>>([]);
}
