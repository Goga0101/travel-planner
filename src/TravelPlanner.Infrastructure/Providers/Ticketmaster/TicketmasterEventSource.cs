using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Ticketmaster;

public sealed class TicketmasterOptions
{
    public const string SectionName = "Providers:Ticketmaster";

    public string BaseUrl { get; set; } = "https://app.ticketmaster.com";
    /// <summary>Free consumer key from developer.ticketmaster.com (Discovery API, 5,000 calls a day).</summary>
    public string? ApiKey { get; set; }
    public int RadiusKm { get; set; } = 15;
    public int CacheHours { get; set; } = 6;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Ticketmaster Discovery API v2: events within a radius of the city centre during the trip, with face-value price
/// ranges and images when Ticketmaster publishes them (Europe, North America, Australia and more).
/// </summary>
public sealed class TicketmasterEventSource(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<TicketmasterOptions> options,
    ILogger<TicketmasterEventSource> logger) : IEventSource
{
    public const string ProviderName = "Ticketmaster";

    public string Name => ProviderName;
    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<IReadOnlyList<CityEvent>> GetAsync(EventQuery query, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return [];
        var key = $"events|{query.Latitude:F3},{query.Longitude:F3}|{query.From}|{query.To}";
        try
        {
            return await cache.GetOrCreateAsync(key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours));
                var c = CultureInfo.InvariantCulture;
                var url = "discovery/v2/events.json?apikey=" + Uri.EscapeDataString(options.Value.ApiKey!) +
                          $"&latlong={query.Latitude.ToString("0.####", c)},{query.Longitude.ToString("0.####", c)}" +
                          $"&radius={options.Value.RadiusKm}&unit=km&size=150&sort=date,asc&locale=*" +
                          $"&startDateTime={query.From:yyyy-MM-dd}T00:00:00Z&endDateTime={query.To:yyyy-MM-dd}T23:59:59Z";
                var body = await httpClient.GetFromJsonAsync<JsonElement>(url, cancellationToken);
                // Long-running events (exhibitions, attractions) list one entry per session: keep the first one in the trip of each.
                return (IReadOnlyList<CityEvent>)Map(body)
                    .Where(e => e.Date >= query.From && e.Date <= query.To)
                    .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderBy(e => e.Date).ThenBy(e => e.Time).First())
                    .OrderBy(e => e.Date).ThenBy(e => e.Time)
                    .ToList();
            }) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Ticketmaster events failed ({Error})", ex.GetType().Name);
            return [];
        }
    }

    public static List<CityEvent> Map(JsonElement body)
    {
        var result = new List<CityEvent>();
        if (!body.TryGetProperty("_embedded", out var embedded) || !embedded.TryGetProperty("events", out var events)) return result;
        foreach (var e in events.EnumerateArray())
        {
            var start = e.TryGetProperty("dates", out var dates) && dates.TryGetProperty("start", out var s) ? s : default;
            if (!DateOnly.TryParse(Str(start, "localDate"), CultureInfo.InvariantCulture, out var date)) continue;
            TimeOnly? time = TimeOnly.TryParse(Str(start, "localTime"), CultureInfo.InvariantCulture, out var t) ? t : null;

            var classification = e.TryGetProperty("classifications", out var cl) && cl.ValueKind == JsonValueKind.Array && cl.GetArrayLength() > 0 ? cl[0] : default;
            var venue = e.TryGetProperty("_embedded", out var ev) && ev.TryGetProperty("venues", out var vs) && vs.GetArrayLength() > 0 ? vs[0] : default;
            var location = venue.ValueKind == JsonValueKind.Object && venue.TryGetProperty("location", out var loc) ? loc : default;
            var price = e.TryGetProperty("priceRanges", out var pr) && pr.ValueKind == JsonValueKind.Array && pr.GetArrayLength() > 0 ? pr[0] : default;

            result.Add(new CityEvent(
                Str(e, "id") ?? Guid.NewGuid().ToString("N"),
                Str(e, "name") ?? "",
                date,
                time,
                Str(venue, "name"),
                double.TryParse(Str(location, "latitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) ? lat : null,
                double.TryParse(Str(location, "longitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ? lon : null,
                ClassName(classification, "segment"),
                ClassName(classification, "genre"),
                BestImage(e),
                Decimal(price, "min"),
                Decimal(price, "max"),
                Str(price, "currency"),
                Str(e, "url")));
        }
        return result.Where(e => e.Name.Length > 0).DistinctBy(e => (e.Name, e.Date)).ToList();
    }

    /// <summary>A 16:9 image around 640 px wide, falling back to the widest one.</summary>
    private static string? BestImage(JsonElement e)
    {
        if (!e.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array) return null;
        var all = images.EnumerateArray()
            .Select(i => (Url: Str(i, "url"), Ratio: Str(i, "ratio"), Width: i.TryGetProperty("width", out var w) && w.TryGetInt32(out var px) ? px : 0))
            .Where(i => i.Url is not null)
            .ToList();
        return (all.Where(i => i.Ratio == "16_9" && i.Width >= 500).OrderBy(i => i.Width).FirstOrDefault().Url)
               ?? all.OrderByDescending(i => i.Width).FirstOrDefault().Url;
    }

    private static string? ClassName(JsonElement classification, string property) =>
        classification.ValueKind == JsonValueKind.Object && classification.TryGetProperty(property, out var p) ? Str(p, "name") is { } n && n != "Undefined" ? n : null : null;

    private static decimal? Decimal(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}

public sealed class NoEventSource : IEventSource
{
    public string Name => "none";
    public bool IsConfigured => false;

    public Task<IReadOnlyList<CityEvent>> GetAsync(EventQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CityEvent>>([]);
}
