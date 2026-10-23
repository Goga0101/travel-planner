using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Infrastructure.Providers.BookingCom;

public sealed class BookingComOptions
{
    public const string SectionName = "Providers:BookingCom";

    public bool Enabled { get; set; }
    /// <summary>Production: https://demandapi.booking.com/3.1 — sandbox: https://demandapi-sandbox.booking.com/3.1</summary>
    public string BaseUrl { get; set; } = "https://demandapi.booking.com/3.1";
    public string? ApiKey { get; set; }
    public string? AffiliateId { get; set; }
    /// <summary>Two-letter booker country sent with each search.</summary>
    public string BookerCountry { get; set; } = "ge";
    public int OfferValidityMinutes { get; set; } = 30;

    public bool IsConfigured => Enabled && ProviderHttp.HasValue(ApiKey) && ProviderHttp.HasValue(AffiliateId);
}

/// <summary>
/// Maps Booking.com Demand API responses (accommodations/search + accommodations/details). Parsing is defensive because
/// field shapes differ slightly between API versions (amounts may be numbers or per-currency objects).
/// </summary>
public static class BookingComMapper
{
    public const string ProviderName = "Booking.com";

    public sealed record PropertyDetails(string? Name, string? Address, double? Latitude, double? Longitude, decimal? Stars, decimal? ReviewScore);

    public static IReadOnlyList<AccommodationOffer> MapSearch(JsonElement root, IReadOnlyDictionary<string, PropertyDetails> details,
        AccommodationSearchRequest request, DateTimeOffset retrievedAtUtc, int validityMinutes)
    {
        var offers = new List<AccommodationOffer>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return offers;

        foreach (var item in data.EnumerateArray())
        {
            var id = ReadId(item);
            if (id is null) continue;
            var currency = ReadString(item, "currency", "booker") ?? ReadString(item, "currency") ?? request.Currency;
            if (!item.TryGetProperty("price", out var price)) continue;

            var total = ReadAmount(price, "total");
            var book = ReadAmount(price, "book");
            if (total is not > 0) continue;

            var fees = new List<MandatoryFee>();
            if (book is > 0 && total > book)
            {
                // "total" already contains charges payable at the property: show them, never add them twice.
                fees.Add(new MandatoryFee(T.Get("booking.chargesAtProperty"), Math.Round(total.Value - book.Value, 2), currency.ToUpperInvariant(),
                    IncludedInPrice: true, PayableAtProperty: true));
            }

            string? cancellation = null;
            DateTimeOffset? freeUntil = null;
            string? mealPlan = null;
            if (item.TryGetProperty("products", out var products) && products.ValueKind == JsonValueKind.Array && products.GetArrayLength() > 0)
            {
                var product = products[0];
                if (product.TryGetProperty("policies", out var policies))
                {
                    if (policies.TryGetProperty("cancellation", out var c))
                    {
                        cancellation = ReadString(c, "type")?.Replace('_', ' ');
                        if (DateTimeOffset.TryParse(ReadString(c, "free_cancellation_until"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var until))
                        {
                            freeUntil = until.ToUniversalTime();
                            cancellation = T.Get("booking.freeCancellation", freeUntil.Value.UtcDateTime);
                        }
                    }
                    if (policies.TryGetProperty("meal_plan", out var meal)) mealPlan = ReadString(meal, "plan")?.Replace('_', ' ');
                }
            }

            details.TryGetValue(id, out var d);
            offers.Add(new AccommodationOffer
            {
                Provider = ProviderName,
                PropertyId = id,
                Name = d?.Name ?? T.Get("booking.propertyFallback", id),
                Address = d?.Address,
                Latitude = d?.Latitude,
                Longitude = d?.Longitude,
                Stars = d?.Stars,
                ReviewScore = d?.ReviewScore,
                TotalPrice = Math.Round(total.Value, 2),
                Currency = currency.ToUpperInvariant(),
                Fees = fees,
                CancellationPolicy = cancellation,
                FreeCancellationUntil = freeUntil,
                MealPlan = mealPlan,
                BookingUrl = ReadString(item, "url"),
                Verification = VerificationStatus.Live,
                RetrievedAtUtc = retrievedAtUtc,
                ExpiresAtUtc = retrievedAtUtc.AddMinutes(validityMinutes)
            });
        }
        return offers;
    }

    public static Dictionary<string, PropertyDetails> MapDetails(JsonElement root)
    {
        var result = new Dictionary<string, PropertyDetails>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return result;
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadId(item);
            if (id is null) continue;
            double? lat = null, lng = null;
            string? address = null;
            if (item.TryGetProperty("location", out var location))
            {
                address = ReadLocalized(location, "address");
                if (location.TryGetProperty("coordinates", out var coords))
                {
                    lat = ReadDouble(coords, "latitude");
                    lng = ReadDouble(coords, "longitude");
                }
            }
            decimal? stars = null, review = null;
            if (item.TryGetProperty("rating", out var rating))
            {
                stars = ReadAmount(rating, "stars");
                review = ReadAmount(rating, "review_score");
            }
            result[id] = new PropertyDetails(ReadLocalized(item, "name"), address, lat, lng, stars, review);
        }
        return result;
    }

    public static IEnumerable<string> ReadIds(JsonElement root) =>
        root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? data.EnumerateArray().Select(ReadId).Where(id => id is not null).Select(id => id!)
            : [];

    private static string? ReadId(JsonElement item) =>
        item.TryGetProperty("id", out var id) ? id.ValueKind == JsonValueKind.Number ? id.GetRawText() : id.GetString() : null;

    private static string? ReadString(JsonElement el, params string[] path)
    {
        var current = el;
        foreach (var p in path)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(p, out current)) return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    /// <summary>Reads a number, numeric string, or a per-currency object ({"booker_currency": n}).</summary>
    private static decimal? ReadAmount(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => d,
            JsonValueKind.Object when v.TryGetProperty("booker_currency", out var b) && b.ValueKind == JsonValueKind.Number => b.GetDecimal(),
            _ => null
        };
    }

    private static double? ReadDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    /// <summary>Reads a plain string or a language map ({"en-gb": "..."}).</summary>
    private static string? ReadLocalized(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind != JsonValueKind.Object) return null;
        if (v.TryGetProperty("en-gb", out var en) && en.ValueKind == JsonValueKind.String) return en.GetString();
        foreach (var p in v.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String) return p.Value.GetString();
            if (p.Value.ValueKind == JsonValueKind.Object && ReadLocalized(v, p.Name) is { } nested) return nested;
        }
        return null;
    }
}

/// <summary>
/// Booking.com Demand API (search-look-redirect). Requires Managed Affiliate Partner approval, an API key and affiliate id.
/// Only normalized fields are kept; raw payloads are not persisted.
/// </summary>
public sealed class BookingComAccommodationProvider(
    HttpClient httpClient,
    IOptions<BookingComOptions> options,
    TimeProvider timeProvider,
    ILogger<BookingComAccommodationProvider> logger) : IAccommodationSearchProvider
{
    public string Name => BookingComMapper.ProviderName;
    public bool IsDemo => false;

    public Task<ProviderResult<IReadOnlyList<AccommodationOffer>>> SearchAsync(AccommodationSearchRequest request, CancellationToken cancellationToken) =>
        ProviderHttp.Guard(Name, async () =>
        {
            if (request.BookingComCityId is null)
                return ProviderResult<IReadOnlyList<AccommodationOffer>>.Failure(Name, ProviderErrorKind.InvalidRequest,
                    $"No Booking.com city id is configured for {request.City}.");

            var o = options.Value;
            var search = await PostAsync("accommodations/search", new
            {
                booker = new { country = o.BookerCountry, platform = "desktop" },
                checkin = ProviderHttp.Iso(request.CheckIn),
                checkout = ProviderHttp.Iso(request.CheckOut),
                city = request.BookingComCityId,
                guests = new { number_of_adults = request.Adults, number_of_rooms = request.Rooms },
                currency = request.Currency,
                extras = new[] { "extra_charges", "products" },
                rows = Math.Clamp(request.MaxResults, 10, 100)
            }, cancellationToken);
            if (search.Error is { } error)
                return ProviderResult<IReadOnlyList<AccommodationOffer>>.Failure(Name, error, "Booking.com search failed.");

            var ids = BookingComMapper.ReadIds(search.Body).Take(request.MaxResults).ToList();
            var details = new Dictionary<string, BookingComMapper.PropertyDetails>();
            if (ids.Count > 0)
            {
                var detailResponse = await PostAsync("accommodations/details", new
                {
                    accommodations = ids.Select(id => long.TryParse(id, out var n) ? (object)n : id).ToArray(),
                    languages = new[] { "en-gb" }
                }, cancellationToken);
                if (detailResponse.Error is null) details = BookingComMapper.MapDetails(detailResponse.Body);
                else logger.LogWarning("Booking.com details request failed ({Error}); offers will lack names and coordinates", detailResponse.Error);
            }

            var offers = BookingComMapper.MapSearch(search.Body, details, request, timeProvider.GetUtcNow(), o.OfferValidityMinutes)
                .Where(x => ids.Contains(x.PropertyId)).ToList();
            return ProviderResult<IReadOnlyList<AccommodationOffer>>.Success(Name, offers);
        }, cancellationToken);

    private async Task<(JsonElement Body, ProviderErrorKind? Error)> PostAsync(string path, object payload, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(payload) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.Value.ApiKey);
        message.Headers.Add("X-Affiliate-Id", options.Value.AffiliateId);
        using var response = await httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Booking.com {Path} returned {Status}", path, (int)response.StatusCode);
            return (default, ProviderHttp.Classify(response.StatusCode));
        }
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return (body, null);
    }
}
