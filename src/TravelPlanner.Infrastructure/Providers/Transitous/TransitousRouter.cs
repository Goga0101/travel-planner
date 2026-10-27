using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Transitous;

public sealed class TransitousOptions
{
    public const string SectionName = "Providers:Transitous";

    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.transitous.org";
    /// <summary>Journeys longer than this are dropped (incomplete local data can produce absurd detours).</summary>
    public int MaxJourneyMinutes { get; set; } = 150;
    public int CacheHours { get; set; } = 6;
}

/// <summary>
/// Transitous (MOTIS 2, community-run, free with fair use): door-to-door public-transport journeys from open GTFS
/// timetables worldwide. Timetables only cover the coming weeks, so a trip further ahead is planned on the next
/// matching weekday at the same time, and the UI says so.
/// </summary>
public sealed class TransitousRouter(HttpClient httpClient, IMemoryCache cache, IOptions<TransitousOptions> options, TimeProvider timeProvider,
    ILogger<TransitousRouter> logger) : ITransitRouter
{
    public const string ProviderName = "Transitous";
    private const int MaxItineraries = 3;
    private static readonly HashSet<string> LongDistance = new(StringComparer.OrdinalIgnoreCase) { "LONG_DISTANCE", "AIRPLANE", "NIGHT_RAIL", "HIGHSPEED_RAIL" };

    public string Name => ProviderName;
    public bool IsConfigured => options.Value.Enabled;

    public async Task<IReadOnlyList<TransitItinerary>> PlanAsync(TransitRouteQuery query, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return [];
        var departure = TimetableDate(query.LocalDeparture, timeProvider.GetUtcNow(), query.UtcOffset);
        var c = CultureInfo.InvariantCulture;
        var key = $"transit|{query.FromLatitude:F4},{query.FromLongitude:F4}|{query.ToLatitude:F4},{query.ToLongitude:F4}|{departure:yyyy-MM-ddTHH:mm}";
        try
        {
            return await cache.GetOrCreateAsync(key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours));
                var utc = new DateTimeOffset(departure, query.UtcOffset).UtcDateTime;
                var url = $"api/v1/plan?fromPlace={query.FromLatitude.ToString("0.#####", c)},{query.FromLongitude.ToString("0.#####", c)}" +
                          $"&toPlace={query.ToLatitude.ToString("0.#####", c)},{query.ToLongitude.ToString("0.#####", c)}" +
                          $"&time={utc:yyyy-MM-ddTHH:mm:ssZ}";
                var body = await httpClient.GetFromJsonAsync<JsonElement>(url, cancellationToken);
                return (IReadOnlyList<TransitItinerary>)Map(body, query.UtcOffset, options.Value.MaxJourneyMinutes);
            }) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Transitous journey planning failed ({Error})", ex.GetType().Name);
            return [];
        }
    }

    /// <summary>The wished departure when it lies in the next 10 days, otherwise the next date with the same weekday and time.</summary>
    public static DateTime TimetableDate(DateTime wished, DateTimeOffset nowUtc, TimeSpan offset)
    {
        var localNow = nowUtc.ToOffset(offset).DateTime;
        if (wished > localNow && wished <= localNow.AddDays(10)) return wished;
        var date = localNow.Date.AddDays(1);
        while (date.DayOfWeek != wished.DayOfWeek) date = date.AddDays(1);
        return date.Add(wished.TimeOfDay);
    }

    public static List<TransitItinerary> Map(JsonElement body, TimeSpan offset, int maxMinutes)
    {
        var result = new List<TransitItinerary>();
        if (!body.TryGetProperty("itineraries", out var itineraries) || itineraries.ValueKind != JsonValueKind.Array) return result;
        foreach (var it in itineraries.EnumerateArray())
        {
            if (!it.TryGetProperty("legs", out var legsJson) || legsJson.ValueKind != JsonValueKind.Array) continue;
            var legs = new List<TransitLeg>();
            foreach (var l in legsJson.EnumerateArray())
            {
                var mode = Str(l, "mode") ?? "WALK";
                if (!TryTime(l, "startTime", offset, out var start) || !TryTime(l, "endTime", offset, out var end)) continue;
                var minutes = (int)Math.Round((end - start).TotalMinutes);
                var stops = l.TryGetProperty("intermediateStops", out var s) && s.ValueKind == JsonValueKind.Array ? s.GetArrayLength() + 1 : (int?)null;
                var leg = new TransitLeg(mode, Str(l, "routeShortName") ?? Str(l, "routeLongName"), Str(l, "headsign"),
                    PlaceName(l, "from"), PlaceName(l, "to"), start, end, Math.Max(minutes, 0), Str(l, "agencyName"),
                    mode == "WALK" ? null : stops, Str(l, "routeColor"));
                // Consecutive walks (e.g. inside a station) become one.
                if (mode == "WALK" && legs.Count > 0 && legs[^1].Mode == "WALK")
                    legs[^1] = legs[^1] with { To = leg.To, ArrivalLocal = end, DurationMinutes = legs[^1].DurationMinutes + leg.DurationMinutes };
                else
                    legs.Add(leg);
            }
            legs.RemoveAll(l => l.Mode == "WALK" && l.DurationMinutes == 0);
            var transit = legs.Where(l => l.Mode != "WALK").ToList();
            if (legs.Count == 0 || transit.Any(l => LongDistance.Contains(l.Mode))) continue;
            var duration = (int)Math.Round((legs[^1].ArrivalLocal - legs[0].DepartureLocal).TotalMinutes);
            if (duration > maxMinutes) continue;
            result.Add(new TransitItinerary(duration, Math.Max(0, transit.Count - 1), legs[0].DepartureLocal, legs[^1].ArrivalLocal, legs));
        }
        // Same lines in the same order are one option (later departures of it add nothing).
        return result
            .GroupBy(i => string.Join(">", i.Legs.Where(l => l.Mode != "WALK").Select(l => $"{l.Mode}:{l.Line}")))
            .Select(g => g.OrderBy(i => i.DurationMinutes).First())
            .OrderBy(i => i.DurationMinutes).ThenBy(i => i.Transfers)
            .Take(MaxItineraries)
            .ToList();
    }

    private static bool TryTime(JsonElement e, string name, TimeSpan offset, out DateTime local)
    {
        local = default;
        if (!DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t)) return false;
        local = t.ToOffset(offset).DateTime;
        return true;
    }

    private static string PlaceName(JsonElement leg, string name)
    {
        var n = leg.TryGetProperty(name, out var p) ? Str(p, "name") : null;
        return n is null or "START" or "END" ? "" : n;
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;
}
