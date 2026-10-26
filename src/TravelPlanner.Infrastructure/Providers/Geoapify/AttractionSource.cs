using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Geoapify;

/// <summary>
/// Sights from Geoapify (OpenStreetMap) that carry a Wikidata id, ranked by the number of Wikipedia editions (Wikidata
/// sitelinks), described with the Wikipedia page summary in the visitor's language, plus nearby public-transport stops.
/// Everything is cached per destination for a day; Wikimedia APIs need no key.
/// </summary>
public sealed class AttractionSource(
    GeoapifyPlacesClient geoapify,
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    IOptions<GeoapifyOptions> options,
    ILogger<AttractionSource> logger) : IAttractionSource
{
    public const string WikimediaClient = "wikimedia";
    private const string Categories = "tourism.sights,tourism.attraction,entertainment.museum,heritage,building.historic,leisure.park";
    private const int SearchRadiusMeters = 7000;
    private const int MaxAttractions = 12;
    private const int StopWalkMeters = 700;

    private sealed record Base(
        string WikidataId, string OsmName, string Kind, double Lat, double Lon, int Editions,
        Dictionary<string, string> Labels, Dictionary<string, string> Descriptions, Dictionary<string, string> WikiTitles,
        string? Website, string? OpeningHours, string? Fee, List<AttractionStop> Stops);

    private sealed record Summary(string? Extract, string? Image, string? Url, string Language);

    public async Task<IReadOnlyList<Attraction>> GetAsync(AttractionQuery query, CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured) return [];
        var lang = query.Language == "ka" ? "ka" : "en";
        try
        {
            var bases = await cache.GetOrCreateAsync($"attractions|base|{query.DestinationCode}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours));
                return await LoadBaseAsync(query, cancellationToken);
            }) ?? [];

            return await cache.GetOrCreateAsync($"attractions|{query.DestinationCode}|{lang}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours));
                var summaries = await Task.WhenAll(bases.Select(b => SummaryAsync(b, lang, cancellationToken)));
                return bases.Zip(summaries, (b, s) => new Attraction(
                    b.WikidataId,
                    b.Labels.GetValueOrDefault(lang) ?? b.Labels.GetValueOrDefault("en") ?? b.OsmName,
                    b.Kind, b.Lat, b.Lon, b.Editions,
                    b.Descriptions.GetValueOrDefault(lang) ?? b.Descriptions.GetValueOrDefault("en"),
                    s?.Extract, s?.Language, s?.Image, s?.Url,
                    NearbyLinks.CuratedHttps(b.Website), b.OpeningHours, b.Fee, b.Stops)).ToList();
            }) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Attractions for {Destination} failed ({Error})", query.DestinationCode, ex.GetType().Name);
            return [];
        }
    }

    private async Task<List<Base>> LoadBaseAsync(AttractionQuery query, CancellationToken ct)
    {
        var features = await geoapify.SearchAsync(Categories, query.CentreLatitude, query.CentreLongitude, SearchRadiusMeters, 500, ct);
        var places = features
            .Select(Place)
            .OfType<(string Id, string Name, string Kind, double Lat, double Lon, string? Website, string? Hours, string? Fee)>()
            .DistinctBy(p => p.Id)
            .ToList();
        if (places.Count == 0) return [];

        var entities = await WikidataAsync(places.Select(p => p.Id).ToList(), ct);
        var top = places
            .Where(p => entities.ContainsKey(p.Id))
            .Select(p => (Place: p, Entity: entities[p.Id]))
            .OrderByDescending(x => x.Entity.Editions)
            .DistinctBy(x => x.Entity.Labels.GetValueOrDefault("en") ?? x.Place.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxAttractions)
            .ToList();

        var stops = await Task.WhenAll(top.Select(x => StopsAsync(x.Place.Lat, x.Place.Lon, ct)));
        return top.Zip(stops, (x, s) => new Base(
            x.Place.Id, x.Place.Name, x.Place.Kind, x.Place.Lat, x.Place.Lon, x.Entity.Editions,
            x.Entity.Labels, x.Entity.Descriptions, x.Entity.WikiTitles, x.Place.Website, x.Place.Hours, x.Place.Fee, s)).ToList();
    }

    /// <summary>OSM place with a Wikidata id, or null.</summary>
    private static (string Id, string Name, string Kind, double Lat, double Lon, string? Website, string? Hours, string? Fee)? Place(JsonElement f)
    {
        if (!f.TryGetProperty("properties", out var p) || !p.TryGetProperty("datasource", out var ds) || !ds.TryGetProperty("raw", out var raw)) return null;
        var id = Str(raw, "wikidata");
        var name = Str(p, "name");
        if (id is null || name is null || !id.StartsWith('Q') || !p.TryGetProperty("lat", out var lat) || !p.TryGetProperty("lon", out var lon)) return null;
        var categories = p.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Array
            ? c.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
            : [];
        return (id, name, Kind(categories), lat.GetDouble(), lon.GetDouble(),
            Str(p, "website") ?? Str(raw, "website"), Str(raw, "opening_hours") is { Length: <= 60 } hours ? hours : null, Str(raw, "fee"));
    }

    public static string Kind(IReadOnlyCollection<string> categories)
    {
        if (categories.Contains("entertainment.museum")) return "museum";
        if (categories.Any(c => c.StartsWith("tourism.sights.castle", StringComparison.Ordinal) || c.StartsWith("tourism.sights.fort", StringComparison.Ordinal))) return "castle";
        if (categories.Any(c => c.StartsWith("tourism.sights.place_of_worship", StringComparison.Ordinal) || c.StartsWith("religion", StringComparison.Ordinal))) return "worship";
        if (categories.Contains("leisure.park")) return "park";
        if (categories.Any(c => c.StartsWith("tourism.sights.memorial", StringComparison.Ordinal) || c.StartsWith("tourism.sights.monument", StringComparison.Ordinal))) return "monument";
        if (categories.Any(c => c.StartsWith("tourism.sights.archaeological_site", StringComparison.Ordinal) || c.StartsWith("heritage", StringComparison.Ordinal))) return "heritage";
        if (categories.Any(c => c.StartsWith("tourism.attraction.viewpoint", StringComparison.Ordinal))) return "viewpoint";
        return "sight";
    }

    private sealed record Entity(int Editions, Dictionary<string, string> Labels, Dictionary<string, string> Descriptions, Dictionary<string, string> WikiTitles);

    private async Task<Dictionary<string, Entity>> WikidataAsync(List<string> ids, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(WikimediaClient);
        var result = new Dictionary<string, Entity>();
        foreach (var batch in ids.Chunk(50))
        {
            var url = "https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=sitelinks|labels|descriptions&languages=en|ka&ids=" +
                      string.Join('|', batch);
            var body = await http.GetFromJsonAsync<JsonElement>(url, ct);
            if (!body.TryGetProperty("entities", out var entities)) continue;
            foreach (var e in entities.EnumerateObject())
            {
                if (!e.Value.TryGetProperty("sitelinks", out var links) || links.ValueKind != JsonValueKind.Object) continue;
                // Only Wikipedia editions count (not Wikivoyage/Commons/...).
                var wikis = links.EnumerateObject().Where(l => l.Name.EndsWith("wiki", StringComparison.Ordinal) && l.Name != "commonswiki" && l.Name != "specieswiki").ToList();
                if (wikis.Count == 0) continue;
                result[e.Name] = new Entity(
                    wikis.Count,
                    LangValues(e.Value, "labels"),
                    LangValues(e.Value, "descriptions"),
                    wikis.Where(l => l.Name is "enwiki" or "kawiki")
                        .ToDictionary(l => l.Name[..2], l => l.Value.GetProperty("title").GetString()!));
            }
        }
        return result;
    }

    private static Dictionary<string, string> LangValues(JsonElement entity, string property)
    {
        var values = new Dictionary<string, string>();
        if (!entity.TryGetProperty(property, out var p) || p.ValueKind != JsonValueKind.Object) return values;
        foreach (var lang in new[] { "en", "ka" })
            if (p.TryGetProperty(lang, out var v) && Str(v, "value") is { } s)
                values[lang] = lang == "en" && property == "labels" && s.Length > 0 ? char.ToUpperInvariant(s[0]) + s[1..] : s;
        return values;
    }

    private async Task<Summary?> SummaryAsync(Base b, string lang, CancellationToken ct)
    {
        // Georgian article if it exists for a Georgian visitor, otherwise English.
        var (wikiLang, title) = lang == "ka" && b.WikiTitles.TryGetValue("ka", out var ka) ? ("ka", ka)
            : b.WikiTitles.TryGetValue("en", out var en) ? ("en", en) : (null, null);
        if (title is null) return null;
        try
        {
            var http = httpFactory.CreateClient(WikimediaClient);
            var body = await http.GetFromJsonAsync<JsonElement>(
                $"https://{wikiLang}.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(title.Replace(' ', '_'))}", ct);
            if (Str(body, "type") == "disambiguation") return null;
            var image = body.TryGetProperty("thumbnail", out var t) ? Str(t, "source") : null;
            var url = body.TryGetProperty("content_urls", out var cu) && cu.TryGetProperty("desktop", out var d) ? Str(d, "page") : null;
            return new Summary(Shorten(Str(body, "extract")), NearbyLinks.CuratedHttps(image), NearbyLinks.CuratedHttps(url), wikiLang!);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>
    /// First two sentences, at most about 320 characters. Sentences shorter than 40 (first) or 20 (second) characters are not split, so
    /// abbreviations such as "ძვ. წ." or "St. " do not end it.
    /// </summary>
    public static string? Shorten(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        var end = 0;
        for (var i = 0; i < 2; i++)
        {
            var next = text.IndexOf(". ", Math.Min(text.Length, end + (i == 0 ? 40 : 20)), StringComparison.Ordinal);
            if (next < 0 || next > 320) break;
            end = next + 1;
        }
        if (end == 0) end = Math.Min(text.Length, 320);
        var result = text[..end].Trim();
        return result.Length < text.Length && !result.EndsWith('.') ? result.TrimEnd(',', ';', ' ') + "…" : result;
    }

    /// <summary>
    /// Nearest metro/train/tram station (OSM often lists no line numbers on stations, so they are kept without) and the
    /// nearest bus stop that lists its lines.
    /// </summary>
    private async Task<List<AttractionStop>> StopsAsync(double lat, double lon, CancellationToken ct)
    {
        var features = await geoapify.SearchAsync(GeoapifyMapper.StopCategories, lat, lon, StopWalkMeters, 40, ct);
        var stops = features.Select(GeoapifyMapper.ToStop).OfType<NearbyTransitStop>()
            .Where(s => s.Mode != "bus" || s.Lines.Count > 0)
            .Select(s => new AttractionStop(s.Name, s.Mode, s.Lines.Take(8).ToList(), NearbyLinks.DistanceMeters(lat, lon, s.Latitude, s.Longitude)))
            .OrderBy(s => s.DistanceMeters)
            .ToList();
        var rail = stops.FirstOrDefault(s => s.Mode != "bus");
        var bus = stops.FirstOrDefault(s => s.Mode == "bus");
        return new[] { rail, bus }.OfType<AttractionStop>().ToList();
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}

public sealed class NoAttractionSource : IAttractionSource
{
    public Task<IReadOnlyList<Attraction>> GetAsync(AttractionQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Attraction>>([]);
}
