using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;
using TravelPlanner.Infrastructure.Providers.Geoapify;

namespace TravelPlanner.Infrastructure.Providers.HotelSearch;

/// <summary>
/// Hotel lookup by name from two OpenStreetMap geocoders, asked in parallel and merged: Geoapify autocomplete (when a key
/// is configured) and Photon (komoot, keyless). Either may fail on its own; results are cached briefly per text and city.
/// </summary>
public sealed class HotelSearchService(
    IHttpClientFactory httpFactory,
    IMemoryCache cache,
    ILogger<HotelSearchService> logger,
    GeoapifyPlacesClient? geoapify = null) : IHotelSearch
{
    public const string PhotonClient = "photon";
    private static readonly string[] Kinds = ["hotel", "hostel", "guest_house", "motel", "apartment", "chalet"];

    public async Task<IReadOnlyList<HotelSearchHit>> SearchAsync(HotelSearchQuery query, CancellationToken cancellationToken)
    {
        var c = CultureInfo.InvariantCulture;
        var key = $"hotel-search|{query.Text.ToLowerInvariant()}|{query.Latitude.ToString("0.00", c)}|{query.Longitude.ToString("0.00", c)}";
        if (cache.TryGetValue(key, out IReadOnlyList<HotelSearchHit>? known) && known is not null) return known;

        var geo = Safe("Geoapify", () => geoapify is null ? Task.FromResult<IReadOnlyList<HotelSearchHit>>([]) : GeoapifyAsync(query, cancellationToken), cancellationToken);
        var photon = Safe("Photon", () => PhotonAsync(query, cancellationToken), cancellationToken);
        await Task.WhenAll(geo, photon);

        var merged = Merge(query.Text, geo.Result.Concat(photon.Result));
        cache.Set(key, merged, TimeSpan.FromHours(1));
        return merged;
    }

    /// <summary>Names that contain the typed text first, then the rest; the same hotel from both sources (same name within 150 m) once.</summary>
    public static IReadOnlyList<HotelSearchHit> Merge(string text, IEnumerable<HotelSearchHit> hits)
    {
        var list = new List<HotelSearchHit>();
        foreach (var h in hits)
        {
            if (list.Any(x => string.Equals(x.Name, h.Name, StringComparison.OrdinalIgnoreCase) &&
                              NearbyLinks.DistanceMeters(x.Latitude, x.Longitude, h.Latitude, h.Longitude) < 150)) continue;
            list.Add(h);
        }
        return list.OrderBy(h => h.Name.Contains(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
    }

    private async Task<IReadOnlyList<HotelSearchHit>> Safe(string source, Func<Task<IReadOnlyList<HotelSearchHit>>> run, CancellationToken ct)
    {
        try { return await run(); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Hotel search via {Source} failed ({Error})", source, ex.GetType().Name);
            return [];
        }
    }

    private async Task<IReadOnlyList<HotelSearchHit>> GeoapifyAsync(HotelSearchQuery q, CancellationToken ct)
    {
        var features = await geoapify!.AutocompleteAsync(q.Text, q.Latitude, q.Longitude, q.RadiusMeters, 15, ct);
        var hits = new List<HotelSearchHit>();
        foreach (var f in features)
        {
            if (!f.TryGetProperty("properties", out var p)) continue;
            var name = Str(p, "name");
            if (name is null || !p.TryGetProperty("lat", out var lat) || !p.TryGetProperty("lon", out var lon)) continue;
            // Accommodation only; places without a category are kept when the name says it (e.g. "Grande Bretagne Hotel").
            var category = Str(p, "category");
            var kind = category is not null && category.StartsWith("accommodation.", StringComparison.Ordinal) ? category["accommodation.".Length..] : null;
            if (kind is null && (category is not null || !LooksLikeLodging(name))) continue;
            hits.Add(new HotelSearchHit("geoapify/" + (Str(p, "place_id") ?? name), name, Str(p, "address_line2"),
                Kinds.Contains(kind) ? kind! : "hotel", lat.GetDouble(), lon.GetDouble(), GeoapifyPlacesClient.ProviderName));
        }
        return hits;
    }

    private async Task<IReadOnlyList<HotelSearchHit>> PhotonAsync(HotelSearchQuery q, CancellationToken ct)
    {
        var c = CultureInfo.InvariantCulture;
        // ~1 degree of latitude is 111 km; the box is a little larger than the radius and the handler trims to the circle.
        var dLat = q.RadiusMeters / 111_000.0;
        var dLon = dLat / Math.Max(0.2, Math.Cos(q.Latitude * Math.PI / 180));
        var bbox = string.Join(',', new[] { q.Longitude - dLon, q.Latitude - dLat, q.Longitude + dLon, q.Latitude + dLat }.Select(v => v.ToString("0.####", c)));
        var tags = string.Concat(Kinds.Select(k => "&osm_tag=tourism:" + k));
        var url = $"api/?q={Uri.EscapeDataString(q.Text)}&lat={q.Latitude.ToString(c)}&lon={q.Longitude.ToString(c)}&bbox={bbox}&limit=12{tags}";
        var body = await httpFactory.CreateClient(PhotonClient).GetFromJsonAsync<JsonElement>(url, ct);
        if (!body.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array) return [];
        var hits = new List<HotelSearchHit>();
        foreach (var f in features.EnumerateArray())
        {
            if (!f.TryGetProperty("properties", out var p) || Str(p, "name") is not { } name) continue;
            if (!f.TryGetProperty("geometry", out var g) || !g.TryGetProperty("coordinates", out var xy) || xy.GetArrayLength() < 2) continue;
            var address = string.Join(", ", new[] { Join(Str(p, "street"), Str(p, "housenumber")), Str(p, "city") }.Where(s => !string.IsNullOrEmpty(s)));
            var kind = Str(p, "osm_value");
            hits.Add(new HotelSearchHit($"{Str(p, "osm_type")}/{Str(p, "osm_id")}", name, address.Length > 0 ? address : null,
                Kinds.Contains(kind) ? kind! : "hotel", xy[1].GetDouble(), xy[0].GetDouble(), "Photon"));
        }
        return hits;
    }

    private static bool LooksLikeLodging(string name) =>
        new[] { "hotel", "hostel", "suites", "inn", "resort", "apartments", "rooms", "guest", "boutique", "ξενοδοχείο" }
            .Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase));

    private static string? Join(string? street, string? number) => street is null ? null : number is null ? street : $"{street} {number}";

    private static string? Str(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        var s = v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null };
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim().Length > 160 ? s.Trim()[..160] : s.Trim();
    }
}
