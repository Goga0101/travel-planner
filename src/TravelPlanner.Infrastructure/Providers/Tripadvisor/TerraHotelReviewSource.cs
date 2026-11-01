using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Tripadvisor;

public sealed class TripadvisorOptions
{
    public const string SectionName = "Providers:Tripadvisor";

    public string BaseUrl { get; set; } = "https://terra.tripadvisor.com";
    /// <summary>Terra API key (terra.tripadvisor.com). Pay per use: each nearby result and each reviews call is billed.</summary>
    public string? ApiKey { get; set; }
    /// <summary>How far from the picked hotel's position a Tripadvisor hotel may be to count as the same one.</summary>
    public double MatchRadiusKm { get; set; } = 0.3;
    /// <summary>Nearby candidates to compare by name (each one is billed).</summary>
    public int MatchCandidates { get; set; } = 3;
    /// <summary>Reviews asked for per call (one billed call per hotel view).</summary>
    public int ReviewsPerCall { get; set; } = 10;
    /// <summary>Location ids may be cached (Terra caching policy); review texts may not.</summary>
    public int LocationIdCacheDays { get; set; } = 30;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>
/// Tripadvisor Terra API: finds the picked hotel among Tripadvisor hotels within a few hundred metres (by name), then
/// reads its most recent reviews. Only the location id is cached; reviews are fetched every time they are shown.
/// </summary>
public sealed class TerraHotelReviewSource(
    HttpClient http,
    IMemoryCache cache,
    IOptions<TripadvisorOptions> options,
    ILogger<TerraHotelReviewSource> logger) : IHotelReviewSource
{
    public const string ProviderName = "Tripadvisor";

    public bool IsConfigured => options.Value.IsConfigured;

    public async Task<HotelReviewResult?> GetRecentAsync(HotelReviewQuery query, CancellationToken cancellationToken)
    {
        if (!IsConfigured) return null;
        try
        {
            var location = await FindAsync(query, cancellationToken);
            if (location is null) return new HotelReviewResult(ProviderName, false, null, null, []);

            var o = options.Value;
            var url = $"api/locations/{location.Value.Id}/reviews?version=1&sort_by=MOST_RECENT&size={o.ReviewsPerCall}&language=en";
            using var request = Request(url);
            using var response = await http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var reviews = TerraMapper.ParseReviews(body);
            return new HotelReviewResult(ProviderName, true, location.Value.Name, HotelPage(reviews), reviews);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "Tripadvisor reviews failed for {Hotel}", query.Name);
            return null;
        }
    }

    /// <summary>The Tripadvisor hotel at the picked position whose name matches; its id is remembered.</summary>
    private async Task<(long Id, string Name)?> FindAsync(HotelReviewQuery q, CancellationToken ct)
    {
        var o = options.Value;
        var key = string.Create(CultureInfo.InvariantCulture, $"terra|loc|{q.Latitude:F4}|{q.Longitude:F4}|{TerraMapper.Normalize(q.Name)}");
        if (cache.TryGetValue(key, out (long Id, string Name)? hit)) return hit;

        var url = string.Create(CultureInfo.InvariantCulture,
            $"api/locations/nearby?version=1&lat={q.Latitude:F6}&lon={q.Longitude:F6}&radius={o.MatchRadiusKm}&unit=KM&category=HOTEL&sort=distance,asc&size={o.MatchCandidates}");
        using var request = Request(url);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var match = TerraMapper.BestMatch(q.Name, TerraMapper.ParseNearby(body));
        (long Id, string Name)? found = match is null ? null : (match.Id, match.Name);
        // Remember misses for a day only: the hotel may be listed later.
        cache.Set(key, found, found is null ? TimeSpan.FromDays(1) : TimeSpan.FromDays(o.LocationIdCacheDays));
        return found;
    }

    private HttpRequestMessage Request(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-API-Key", options.Value.ApiKey);
        return request;
    }

    private static string? HotelPage(IReadOnlyList<HotelReview> reviews) =>
        reviews.Select(r => TerraMapper.HotelPageFromReview(r.Url)).FirstOrDefault(u => u is not null);
}

public sealed record TerraLocation(long Id, string Name, double DistanceKm);

public static class TerraMapper
{
    private static readonly HashSet<string> Filler =
        ["hotel", "hotels", "the", "and", "&", "by", "a", "an", "of", "de", "la", "le", "el", "il", "das", "der", "die", "resort", "spa", "suites", "apartments"];

    public static List<TerraLocation> ParseNearby(JsonElement body)
    {
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var list = new List<TerraLocation>();
        foreach (var d in data.EnumerateArray())
        {
            if (!d.TryGetProperty("location", out var loc) || loc.ValueKind != JsonValueKind.Object) continue;
            if (!loc.TryGetProperty("id", out var idEl) || !TryLong(idEl, out var id)) continue;
            var name = Localized(loc, "names", null);
            if (name is null) continue;
            var km = d.TryGetProperty("distance_kilometers", out var k) && k.ValueKind == JsonValueKind.Number ? k.GetDouble() : double.MaxValue;
            list.Add(new TerraLocation(id, name, km));
        }
        return list;
    }

    /// <summary>
    /// The candidate whose name shares most words with the picked hotel (filler words like "hotel" ignored).
    /// Half the words in common is enough; a very close candidate (under 60 m) needs only one word.
    /// </summary>
    public static TerraLocation? BestMatch(string name, IReadOnlyList<TerraLocation> candidates)
    {
        var wanted = Words(name);
        if (wanted.Count == 0) return null;
        TerraLocation? best = null;
        var bestScore = 0.0;
        foreach (var c in candidates)
        {
            var have = Words(c.Name);
            if (have.Count == 0) continue;
            var common = wanted.Intersect(have).Count();
            var score = (double)common / Math.Min(wanted.Count, have.Count);
            var enough = score >= 0.5 || (common >= 1 && c.DistanceKm <= 0.06);
            if (enough && (score > bestScore || (Math.Abs(score - bestScore) < 1e-9 && c.DistanceKm < best!.DistanceKm)))
            {
                best = c;
                bestScore = score;
            }
        }
        return best;
    }

    public static List<HotelReview> ParseReviews(JsonElement body)
    {
        if (!body.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var list = new List<HotelReview>();
        foreach (var r in data.EnumerateArray())
        {
            if (!r.TryGetProperty("id", out var idEl)) continue;
            var id = idEl.ValueKind == JsonValueKind.Number ? idEl.GetRawText() : idEl.GetString();
            var text = Localized(r, "text", "en");
            if (id is null || text is null) continue;
            if (!r.TryGetProperty("publish_ts", out var ts) || ts.ValueKind != JsonValueKind.String ||
                !DateTimeOffset.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)) continue;
            var rating = r.TryGetProperty("rating", out var ra) && ra.ValueKind == JsonValueKind.Number ? (int)Math.Round(ra.GetDouble()) : 0;
            if (rating is < 1 or > 5) continue;

            string? author = null, authorGeo = null, avatar = null;
            if (r.TryGetProperty("user", out var u) && u.ValueKind == JsonValueKind.Object)
            {
                author = Str(u, "username");
                authorGeo = Str(u, "geo");
                avatar = Https(UrlOf(u, "avatar_url"));
            }
            list.Add(new HotelReview(
                id, rating, Localized(r, "title", "en"), text, LanguageOf(r, "text") ?? "en", published,
                Str(r, "travel_date"), Str(r, "trip_type"), author, authorGeo, avatar,
                TripadvisorLink(Str(r, "url")), Https(UrlOf(r, "rating_icon_url")), OwnerResponse(r)));
        }
        return list;
    }

    /// <summary>
    /// The hotel's page from one of its review links:
    /// …/ShowUserReviews-g187497-d190567-r1023-Hotel_Name-Barcelona.html → …/Hotel_Review-g187497-d190567-Reviews-Hotel_Name-Barcelona.html
    /// </summary>
    public static string? HotelPageFromReview(string? reviewUrl)
    {
        if (reviewUrl is null) return null;
        var m = System.Text.RegularExpressions.Regex.Match(reviewUrl, @"^(https://[^/]+/)ShowUserReviews-(g\d+)-(d\d+)-r\d+-(.+)$");
        return m.Success ? $"{m.Groups[1].Value}Hotel_Review-{m.Groups[2].Value}-{m.Groups[3].Value}-Reviews-{m.Groups[4].Value}" : null;
    }

    public static string Normalize(string s)
    {
        var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static HashSet<string> Words(string s) =>
        Normalize(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => !Filler.Contains(w)).ToHashSet();

    /// <summary>Terra texts are arrays of { language, primary, value }: the wanted language, else the primary one, else the first.</summary>
    private static string? Localized(JsonElement e, string name, string? language)
    {
        if (!e.TryGetProperty(name, out var arr)) return null;
        if (arr.ValueKind == JsonValueKind.String) return Clean(arr.GetString());
        if (arr.ValueKind != JsonValueKind.Array) return null;
        var items = arr.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).ToList();
        var pick = items.FirstOrDefault(i => language is not null && Str(i, "language") == language);
        if (pick.ValueKind != JsonValueKind.Object) pick = items.FirstOrDefault(i => i.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True);
        if (pick.ValueKind != JsonValueKind.Object) pick = items.FirstOrDefault();
        return pick.ValueKind == JsonValueKind.Object ? Clean(Str(pick, "value")) : null;
    }

    private static string? LanguageOf(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
        var items = arr.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object).ToList();
        var en = items.FirstOrDefault(i => Str(i, "language") == "en");
        return en.ValueKind == JsonValueKind.Object ? "en" : items.Select(i => Str(i, "language")).FirstOrDefault(l => l is not null);
    }

    private static string? OwnerResponse(JsonElement r)
    {
        if (!r.TryGetProperty("owner_response", out var o)) return null;
        return o.ValueKind switch
        {
            JsonValueKind.String => Clean(o.GetString()),
            JsonValueKind.Object => Localized(o, "text", "en") ?? Clean(Str(o, "value")),
            _ => null,
        };
    }

    private static string? UrlOf(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? Str(v, "url")
        : e.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string? Https(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps ? u.AbsoluteUri : null;

    /// <summary>Only https links on tripadvisor.* are passed on.</summary>
    private static string? TripadvisorLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("tripadvisor.com", StringComparison.OrdinalIgnoreCase) || u.Host.Contains(".tripadvisor.", StringComparison.OrdinalIgnoreCase) ||
         u.Host.StartsWith("tripadvisor.", StringComparison.OrdinalIgnoreCase))
            ? u.AbsoluteUri
            : null;

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString()!.Trim() : null;

    private static bool TryLong(JsonElement e, out long value)
    {
        value = 0;
        return e.ValueKind == JsonValueKind.Number ? e.TryGetInt64(out value)
            : e.ValueKind == JsonValueKind.String && long.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
