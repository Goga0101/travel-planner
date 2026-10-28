using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Transitous;

/// <summary>
/// Place search (Photon for any language and script, Transitous for stops) and the vehicles running in an area
/// (Transitous map trips: operators' live feeds where published, otherwise the timetable).
/// </summary>
public sealed class TransitousLive(HttpClient httpClient, IHttpClientFactory clients, IMemoryCache cache, IOptions<TransitousOptions> options,
    ILogger<TransitousLive> logger) : ILiveTransit
{
    public const string PhotonClient = "photon-geocoder";
    private const int MaxPlaces = 8;

    public string Name => TransitousRouter.ProviderName;
    public bool IsConfigured => options.Value.Enabled;

    public async Task<IReadOnlyList<GeocodedPlace>> GeocodeAsync(string text, string language, double? nearLatitude, double? nearLongitude,
        CancellationToken cancellationToken)
    {
        var c = CultureInfo.InvariantCulture;
        var near = nearLatitude is { } la && nearLongitude is { } lo ? $"{la.ToString("0.##", c)},{lo.ToString("0.##", c)}" : null;
        var key = $"geocode|{language}|{near}|{text.Trim().ToLowerInvariant()}";
        return await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            var photon = PhotonAsync(text, language, nearLatitude, nearLongitude, cancellationToken);
            var stops = IsConfigured ? StopsAsync(text, language, near, cancellationToken) : Task.FromResult<List<GeocodedPlace>>([]);
            var places = (await photon).Concat(await stops)
                .Aggregate(new List<GeocodedPlace>(), (list, p) =>
                {
                    // The same spot from both sources (or a building and its entrance) is shown once.
                    if (!list.Any(x => Math.Abs(x.Latitude - p.Latitude) < 0.0004 && Math.Abs(x.Longitude - p.Longitude) < 0.0004)) list.Add(p);
                    return list;
                });
            return (IReadOnlyList<GeocodedPlace>)places.Take(MaxPlaces).ToList();
        }) ?? [];
    }

    public async Task<GeocodedPlace?> ReverseAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;
        var c = CultureInfo.InvariantCulture;
        var key = $"reverse|{latitude:F4},{longitude:F4}";
        try
        {
            return await cache.GetOrCreateAsync(key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
                var body = await httpClient.GetFromJsonAsync<JsonElement>(
                    $"api/v1/reverse-geocode?place={latitude.ToString("0.#####", c)},{longitude.ToString("0.#####", c)}", cancellationToken);
                return body.ValueKind == JsonValueKind.Array ? body.EnumerateArray().Select(MotisPlace).FirstOrDefault(p => p is not null) : null;
            });
        }
        catch (Exception ex) when (Transient(ex) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Transitous reverse geocoding failed ({Error})", ex.GetType().Name);
            return null;
        }
    }

    public async Task<IReadOnlyList<VehicleSegment>> VehiclesAsync(GeoBox box, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return [];
        var c = CultureInfo.InvariantCulture;
        string P(double v) => v.ToString("0.###", c);
        var url = $"api/v1/map/trips?min={P(box.MinLatitude)},{P(box.MinLongitude)}&max={P(box.MaxLatitude)},{P(box.MaxLongitude)}" +
                  $"&startTime={from.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&endTime={to.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}&zoom=15";
        try
        {
            // Everyone looking at the same area within a few seconds shares one upstream request.
            return await cache.GetOrCreateAsync("vehicles|" + url, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20);
                var body = await httpClient.GetFromJsonAsync<JsonElement>(url, cancellationToken);
                return (IReadOnlyList<VehicleSegment>)MapSegments(body);
            }) ?? [];
        }
        catch (Exception ex) when (Transient(ex) && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Transitous live vehicles failed ({Error})", ex.GetType().Name);
            return [];
        }
    }

    public static List<VehicleSegment> MapSegments(JsonElement body)
    {
        var result = new List<VehicleSegment>();
        if (body.ValueKind != JsonValueKind.Array) return result;
        foreach (var s in body.EnumerateArray())
        {
            var trip = s.TryGetProperty("trips", out var trips) && trips.ValueKind == JsonValueKind.Array && trips.GetArrayLength() > 0 ? trips[0] : default;
            var tripId = Str(trip, "tripId");
            var polyline = Str(s, "polyline");
            if (tripId is null || polyline is null) continue;
            if (!DateTimeOffset.TryParse(Str(s, "departure"), c, DateTimeStyles.AssumeUniversal, out var dep) ||
                !DateTimeOffset.TryParse(Str(s, "arrival"), c, DateTimeStyles.AssumeUniversal, out var arr)) continue;
            result.Add(new VehicleSegment(tripId, Str(s, "mode") ?? "BUS", Str(trip, "routeShortName"), Str(s, "routeColor"),
                s.TryGetProperty("from", out var f) ? Str(f, "name") ?? "" : "", s.TryGetProperty("to", out var t) ? Str(t, "name") ?? "" : "",
                dep, arr, s.TryGetProperty("realTime", out var rt) && rt.ValueKind == JsonValueKind.True, polyline));
        }
        return result;
    }

    private static readonly CultureInfo c = CultureInfo.InvariantCulture;

    private async Task<List<GeocodedPlace>> PhotonAsync(string text, string language, double? lat, double? lon, CancellationToken ct)
    {
        // Photon labels results in en/de/fr; other languages get the local (OpenStreetMap default) names, which is what signs show.
        var lang = language is "en" or "de" or "fr" ? $"&lang={language}" : "";
        var bias = lat is { } la && lon is { } lo ? $"&lat={la.ToString("0.###", c)}&lon={lo.ToString("0.###", c)}" : "";
        try
        {
            var body = await clients.CreateClient(PhotonClient).GetFromJsonAsync<JsonElement>(
                $"api/?q={Uri.EscapeDataString(text.Trim())}&limit={MaxPlaces}{lang}{bias}", ct);
            if (!body.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array) return [];
            return features.EnumerateArray().Select(PhotonPlace).OfType<GeocodedPlace>().ToList();
        }
        catch (Exception ex) when (Transient(ex) && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Photon geocoding failed ({Error})", ex.GetType().Name);
            return [];
        }
    }

    private async Task<List<GeocodedPlace>> StopsAsync(string text, string language, string? near, CancellationToken ct)
    {
        try
        {
            var url = $"api/v1/geocode?text={Uri.EscapeDataString(text.Trim())}&language={language}&type=STOP" + (near is null ? "" : $"&place={near}");
            var body = await httpClient.GetFromJsonAsync<JsonElement>(url, ct);
            return body.ValueKind == JsonValueKind.Array ? body.EnumerateArray().Select(MotisPlace).OfType<GeocodedPlace>().Take(4).ToList() : [];
        }
        catch (Exception ex) when (Transient(ex) && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Transitous geocoding failed ({Error})", ex.GetType().Name);
            return [];
        }
    }

    public static GeocodedPlace? PhotonPlace(JsonElement feature)
    {
        if (!feature.TryGetProperty("properties", out var p) || !feature.TryGetProperty("geometry", out var g) ||
            !g.TryGetProperty("coordinates", out var xy) || xy.ValueKind != JsonValueKind.Array || xy.GetArrayLength() < 2) return null;
        var street = Str(p, "street") is { } st ? Str(p, "housenumber") is { } hn ? $"{st} {hn}" : st : null;
        var name = Str(p, "name") ?? street;
        if (name is null) return null;
        var area = string.Join(", ", new[] { name == street ? null : street, Str(p, "district") ?? Str(p, "locality"), Str(p, "city"), Str(p, "country") }
            .OfType<string>().Where(a => a != name).Distinct());
        var kind = Str(p, "osm_key") switch
        {
            "highway" when Str(p, "osm_value") is "bus_stop" => "stop",
            "public_transport" or "railway" => "stop",
            "place" or "boundary" => "city",
            _ => Str(p, "type") is "house" or "street" ? "address" : "place"
        };
        return new GeocodedPlace(name, area.Length > 0 ? area : null, Str(p, "countrycode")?.ToUpperInvariant(), xy[1].GetDouble(), xy[0].GetDouble(), kind, null);
    }

    public static GeocodedPlace? MotisPlace(JsonElement e)
    {
        var name = Str(e, "name");
        if (name is null || !e.TryGetProperty("lat", out var lat) || !e.TryGetProperty("lon", out var lon)) return null;
        var areas = e.TryGetProperty("areas", out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().OrderByDescending(x => x.TryGetProperty("adminLevel", out var l) ? l.GetDouble() : 0)
                .Select(x => Str(x, "name")).OfType<string>().Where(n => n != name).Distinct().ToList()
            : [];
        var kind = Str(e, "type") switch { "STOP" => "stop", "ADDRESS" => "address", _ => "place" };
        return new GeocodedPlace(name, areas.Count > 0 ? string.Join(", ", areas) : null, Str(e, "country"), lat.GetDouble(), lon.GetDouble(), kind, Str(e, "tz"));
    }

    private static bool Transient(Exception ex) => ex is HttpRequestException or JsonException or TaskCanceledException or InvalidOperationException
        or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}
