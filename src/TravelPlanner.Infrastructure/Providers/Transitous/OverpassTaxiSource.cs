using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Transitous;

/// <summary>
/// Taxi stands (amenity=taxi) and taxi companies (office=taxi) around a point from the public Overpass API (OpenStreetMap).
/// Results are cached per ~2 km cell for a day; the public instances ask for an identifying User-Agent and modest use.
/// They are often busy, so each is given a few seconds and a mirror is tried next; on failure the list is empty (not cached).
/// </summary>
public sealed class OverpassTaxiSource(HttpClient httpClient, IMemoryCache cache, ILogger<OverpassTaxiSource> logger) : ILocalTaxiSource
{
    private const int MaxResults = 40;
    private static readonly string[] Endpoints = ["https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter"];
    private static readonly TimeSpan PerEndpoint = TimeSpan.FromSeconds(6);

    public async Task<IReadOnlyList<LocalTaxi>> FindAsync(double latitude, double longitude, int radiusMeters, CancellationToken cancellationToken)
    {
        var c = CultureInfo.InvariantCulture;
        var (lat, lon) = (Math.Round(latitude, 2), Math.Round(longitude, 2));
        try
        {
            return await cache.GetOrCreateAsync($"taxis|{lat:F2},{lon:F2}|{radiusMeters}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
                var around = $"(around:{radiusMeters},{lat.ToString(c)},{lon.ToString(c)})";
                var query = $"[out:json][timeout:15];(nwr[\"amenity\"=\"taxi\"]{around};nwr[\"office\"=\"taxi\"]{around};nwr[\"shop\"=\"taxi\"]{around};);out center tags {MaxResults * 2};";
                foreach (var endpoint in Endpoints)
                {
                    using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    budget.CancelAfter(PerEndpoint);
                    try
                    {
                        using var response = await httpClient.PostAsync(endpoint,
                            new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)]), budget.Token);
                        response.EnsureSuccessStatusCode();
                        var body = await response.Content.ReadFromJsonAsync<JsonElement>(budget.Token);
                        return (IReadOnlyList<LocalTaxi>)Map(body, lat, lon);
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException
                                                   or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                               && !cancellationToken.IsCancellationRequested)
                    {
                        logger.LogWarning("Overpass {Endpoint} failed ({Error})", endpoint, ex.GetType().Name);
                    }
                }
                // Not cached: the next visitor tries again.
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(1);
                return (IReadOnlyList<LocalTaxi>)[];
            }) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Overpass taxi lookup failed ({Error})", ex.GetType().Name);
            return [];
        }
    }

    /// <summary>Companies and stands with a phone first, then the nearest; unnamed stands without a phone are kept (they show where to find a cab).</summary>
    public static List<LocalTaxi> Map(JsonElement body, double lat, double lon)
    {
        if (!body.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array) return [];
        var list = new List<LocalTaxi>();
        foreach (var e in elements.EnumerateArray())
        {
            if (!e.TryGetProperty("tags", out var tags)) continue;
            var point = e.TryGetProperty("center", out var center) ? center : e;
            if (!point.TryGetProperty("lat", out var la) || !point.TryGetProperty("lon", out var lo)) continue;
            var company = Str(tags, "office") == "taxi" || Str(tags, "shop") == "taxi";
            list.Add(new LocalTaxi(Str(tags, "name"), Str(tags, "operator"), Str(tags, "phone") ?? Str(tags, "contact:phone"),
                Https(Str(tags, "website") ?? Str(tags, "contact:website")), la.GetDouble(), lo.GetDouble(), company));
        }
        return list
            .OrderBy(t => t.Phone is null)
            .ThenBy(t => !t.IsCompany)
            .ThenBy(t => Math.Pow(t.Latitude - lat, 2) + Math.Pow((t.Longitude - lon) * Math.Cos(lat * Math.PI / 180), 2))
            .Take(MaxResults)
            .ToList();
    }

    private static string? Https(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo) ? u.AbsoluteUri : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}
