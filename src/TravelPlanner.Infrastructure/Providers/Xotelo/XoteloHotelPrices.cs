using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;
using TravelPlanner.Infrastructure.Catalog;

namespace TravelPlanner.Infrastructure.Providers.Xotelo;

public sealed class XoteloOptions
{
    public const string SectionName = "Providers:Xotelo";

    /// <summary>
    /// Xotelo is a free, unofficial price-comparison feed (TripAdvisor data). Prices are indicative only and the
    /// service may change or stop without notice; the site then simply shows no estimates.
    /// </summary>
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "https://data.xotelo.com";
    /// <summary>Hotels per city whose prices are fetched for the plan dates (one request each).</summary>
    public int HotelsPerCity { get; set; } = 8;
    public int ListCacheHours { get; set; } = 24;
    public int RatesCacheHours { get; set; } = 6;
    public int MaxConcurrentRequests { get; set; } = 8;
}

/// <summary>Hotel list (popular hotels per city) and per-date agency rates from the Xotelo API.</summary>
public sealed class XoteloHotelPriceSource(
    HttpClient http,
    CatalogFileReader catalog,
    IMemoryCache cache,
    IOptions<XoteloOptions> options,
    ILogger<XoteloHotelPriceSource> logger) : IHotelPriceSource
{
    public const string SourceName = "Xotelo (TripAdvisor)";

    public async Task<HotelPriceResult?> GetAsync(HotelPriceQuery query, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var geoKey = catalog.Document.Destinations
            .FirstOrDefault(d => string.Equals(d.Code, query.DestinationCode, StringComparison.OrdinalIgnoreCase))?.TripAdvisorGeoKey;
        if (!o.Enabled || string.IsNullOrWhiteSpace(geoKey) || query.CheckOut <= query.CheckIn) return null;

        try
        {
            var hotels = await cache.GetOrCreateAsync($"xotelo|list|{geoKey}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, o.ListCacheHours));
                return await ListAsync(geoKey, o.HotelsPerCity, cancellationToken);
            }) ?? [];

            using var gate = new SemaphoreSlim(Math.Max(1, o.MaxConcurrentRequests));
            var priced = await Task.WhenAll(hotels.Select(async h =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var key = $"xotelo|rates|{h.Key}|{query.CheckIn:yyyy-MM-dd}|{query.CheckOut:yyyy-MM-dd}|{query.Adults}|{query.Rooms}|{query.Currency}";
                    var rates = await cache.GetOrCreateAsync(key, async entry =>
                    {
                        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(Math.Max(1, o.RatesCacheHours));
                        return await RatesAsync(h.Key, query, cancellationToken);
                    }) ?? [];
                    return h with { Rates = rates };
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    // One hotel failing must not hide the others.
                    return h;
                }
                finally
                {
                    gate.Release();
                }
            }));

            return new HotelPriceResult(SourceName, query.Currency, DateTimeOffset.UtcNow, priced.Where(h => h.Rates.Count > 0).ToList());
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Xotelo hotel prices for {Destination} failed ({Error})", query.DestinationCode, ex.GetType().Name);
            return null;
        }
    }

    private async Task<List<HotelPriceOffer>> ListAsync(string geoKey, int limit, CancellationToken ct)
    {
        var body = await http.GetFromJsonAsync<JsonElement>($"api/list?location_key={Uri.EscapeDataString(geoKey)}&limit={limit}&offset=0&sort=best_value", ct);
        return XoteloMapper.ParseList(body);
    }

    private async Task<List<HotelRate>> RatesAsync(string hotelKey, HotelPriceQuery q, CancellationToken ct)
    {
        var url = $"api/rates?hotel_key={Uri.EscapeDataString(hotelKey)}&chk_in={q.CheckIn:yyyy-MM-dd}&chk_out={q.CheckOut:yyyy-MM-dd}" +
                  $"&adults={q.Adults}&rooms={q.Rooms}&currency={Uri.EscapeDataString(q.Currency)}";
        var body = await http.GetFromJsonAsync<JsonElement>(url, ct);
        return XoteloMapper.ParseRates(body, q.Currency);
    }
}

public static class XoteloMapper
{
    public static List<HotelPriceOffer> ParseList(JsonElement body)
    {
        if (!body.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("list", out var list) || list.ValueKind != JsonValueKind.Array) return [];

        var hotels = new List<HotelPriceOffer>();
        foreach (var h in list.EnumerateArray())
        {
            var key = Str(h, "key");
            var name = Str(h, "name");
            if (key is null || name is null) continue;
            double? rating = null; int? reviews = null;
            if (h.TryGetProperty("review_summary", out var r) && r.ValueKind == JsonValueKind.Object)
            {
                rating = Dbl(r, "rating");
                reviews = Dbl(r, "count") is { } c ? (int)c : null;
            }
            decimal? min = null, max = null;
            if (h.TryGetProperty("price_ranges", out var pr) && pr.ValueKind == JsonValueKind.Object)
            {
                min = Dbl(pr, "minimum") is { } a ? (decimal)a : null;
                max = Dbl(pr, "maximum") is { } b ? (decimal)b : null;
            }
            double? lat = null, lon = null;
            if (h.TryGetProperty("geo", out var geo) && geo.ValueKind == JsonValueKind.Object)
            {
                lat = Dbl(geo, "latitude");
                lon = Dbl(geo, "longitude");
            }
            hotels.Add(new HotelPriceOffer(key, name, rating, reviews, lat, lon, min, max, []));
        }
        return hotels;
    }

    /// <summary>Agency rates; only positive prices in the requested currency are kept.</summary>
    public static List<HotelRate> ParseRates(JsonElement body, string currency)
    {
        if (!body.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return [];
        if (Str(result, "currency") is { } c && !string.Equals(c, currency, StringComparison.OrdinalIgnoreCase)) return [];
        if (!result.TryGetProperty("rates", out var rates) || rates.ValueKind != JsonValueKind.Array) return [];
        return rates.EnumerateArray()
            .Select(r => (code: Str(r, "code"), name: Str(r, "name"), rate: Dbl(r, "rate")))
            .Where(r => r.code is not null && r.rate is > 0)
            .Select(r => new HotelRate(r.code!, AgencyName(r.code!, r.name), Math.Round((decimal)r.rate!.Value, 2)))
            .OrderBy(r => r.Nightly)
            .ToList();
    }

    private static string AgencyName(string code, string? name) => code switch
    {
        "BookingCom" => "Booking.com",
        "CtripTA" => "Trip.com",
        "Mirai" => name ?? "Official website",
        _ => name ?? code
    };

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : null;

    private static double? Dbl(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble()
        : e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d
        : null;
}

/// <summary>Used when indicative prices are disabled: the UI then shows only the provider search links.</summary>
public sealed class NoHotelPriceSource : IHotelPriceSource
{
    public Task<HotelPriceResult?> GetAsync(HotelPriceQuery query, CancellationToken cancellationToken) => Task.FromResult<HotelPriceResult?>(null);
}
