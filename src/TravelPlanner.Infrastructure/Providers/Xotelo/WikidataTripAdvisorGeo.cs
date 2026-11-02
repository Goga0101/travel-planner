using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace TravelPlanner.Infrastructure.Providers.Xotelo;

/// <summary>
/// The TripAdvisor location key ("g294195" for Tbilisi) of a city outside the catalog, from Wikidata's "TripAdvisor ID"
/// (P3134): the most populous place with one within 20 km of the city's point. Cached for a month (a miss for a day).
/// </summary>
public sealed class WikidataTripAdvisorGeo(HttpClient http, IMemoryCache cache, ILogger<WikidataTripAdvisorGeo> logger)
{
    public const string ClientName = "wikidata";

    public async Task<string?> FindAsync(double latitude, double longitude, CancellationToken cancellationToken)
    {
        var key = string.Create(CultureInfo.InvariantCulture, $"wikidata|ta-geo|{latitude:0.00}|{longitude:0.00}");
        if (cache.TryGetValue(key, out string? known)) return known;

        string? found = null;
        try
        {
            found = await QueryAsync(latitude, longitude, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Wikidata TripAdvisor lookup failed ({Error})", ex.GetType().Name);
            return null;
        }
        cache.Set(key, found, found is null ? TimeSpan.FromDays(1) : TimeSpan.FromDays(30));
        return found;
    }

    private async Task<string?> QueryAsync(double latitude, double longitude, CancellationToken ct)
    {
        var point = string.Create(CultureInfo.InvariantCulture, $"Point({longitude} {latitude})");
        // Only places with a population: cities and towns, not hotels or sights (which also carry TripAdvisor ids).
        var sparql = $$"""
            SELECT ?ta ?pop WHERE {
              SERVICE wikibase:around { ?item wdt:P625 ?loc . bd:serviceParam wikibase:center "{{point}}"^^geo:wktLiteral . bd:serviceParam wikibase:radius "20" . }
              ?item wdt:P3134 ?ta . ?item wdt:P1082 ?pop .
            } ORDER BY DESC(?pop) LIMIT 1
            """;
        using var request = new HttpRequestMessage(HttpMethod.Get, "sparql?format=json&query=" + Uri.EscapeDataString(sparql));
        request.Headers.Accept.ParseAdd("application/sparql-results+json");
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var rows = body.GetProperty("results").GetProperty("bindings");
        if (rows.GetArrayLength() == 0) return null;
        var id = rows[0].GetProperty("ta").GetProperty("value").GetString();
        return ParseId(id);
    }

    /// <summary>Wikidata stores the numeric part ("294195"); Xotelo wants the location key ("g294195").</summary>
    public static string? ParseId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var digits = id.Trim().TrimStart('g', 'G');
        return digits.Length > 0 && digits.All(char.IsAsciiDigit) ? "g" + digits : null;
    }
}
