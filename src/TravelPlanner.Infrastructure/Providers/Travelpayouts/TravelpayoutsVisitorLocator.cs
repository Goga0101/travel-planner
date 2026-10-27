using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Travelpayouts;

public sealed class WhereAmIResponse
{
    [JsonPropertyName("iata")] public string? Iata { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    /// <summary>"longitude:latitude".</summary>
    [JsonPropertyName("coordinates")] public string? Coordinates { get; set; }
}

/// <summary>
/// Travelpayouts "whereami" (free, no key): the nearest city with an airport for an IP address. Used only to preselect
/// the departure airport. Results are cached per address for a day.
/// </summary>
public sealed class TravelpayoutsVisitorLocator(HttpClient httpClient, IMemoryCache cache, ILogger<TravelpayoutsVisitorLocator> logger)
    : IVisitorLocator
{
    public async Task<VisitorLocation?> LocateAsync(string? ipAddress, CancellationToken cancellationToken)
    {
        var key = "whereami|" + (ipAddress ?? "self");
        if (cache.TryGetValue(key, out VisitorLocation? cached)) return cached;

        try
        {
            var url = "whereami?locale=en" + (ipAddress is null ? "" : "&ip=" + Uri.EscapeDataString(ipAddress));
            var body = await httpClient.GetFromJsonAsync<WhereAmIResponse>(url, cancellationToken);
            var location = Parse(body);
            cache.Set(key, location, TimeSpan.FromHours(location is null ? 1 : 24));
            return location;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogInformation("Visitor location lookup failed: {Message}", ex.Message);
            return null;
        }
    }

    internal static VisitorLocation? Parse(WhereAmIResponse? body)
    {
        var parts = body?.Coordinates?.Split(':');
        if (parts is not { Length: 2 } ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat))
            return null;
        return new VisitorLocation(string.IsNullOrWhiteSpace(body!.Iata) ? null : body.Iata.ToUpperInvariant(), body.Name, lat, lon);
    }
}
