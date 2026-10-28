using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Transitous;

/// <summary>
/// Bus stations around a point from Photon's reverse search (OpenStreetMap amenity=bus_station, else a public-transport
/// station), cached per ~1 km cell for a day. Works worldwide wherever OpenStreetMap maps the station.
/// </summary>
public sealed class PhotonStationFinder(IHttpClientFactory clients, IMemoryCache cache, ILogger<PhotonStationFinder> logger) : IStationFinder
{
    private static readonly string[] Tags = ["amenity:bus_station", "public_transport:station"];

    public async Task<GeocodedPlace?> BusStationNearAsync(double latitude, double longitude, int radiusKm, CancellationToken cancellationToken)
    {
        var c = CultureInfo.InvariantCulture;
        var (lat, lon) = (Math.Round(latitude, 2), Math.Round(longitude, 2));
        return await cache.GetOrCreateAsync($"busstation|{lat:F2},{lon:F2}|{radiusKm}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
            foreach (var tag in Tags)
            {
                try
                {
                    var body = await clients.CreateClient(TransitousLive.PhotonClient).GetFromJsonAsync<JsonElement>(
                        $"reverse?lat={latitude.ToString("0.#####", c)}&lon={longitude.ToString("0.#####", c)}&radius={radiusKm}&osm_tag={tag}&limit=5",
                        cancellationToken);
                    if (!body.TryGetProperty("features", out var features) || features.ValueKind != JsonValueKind.Array) continue;
                    // Named stations first (unnamed ones are often bays of a bigger station).
                    var place = features.EnumerateArray().Select(TransitousLive.PhotonPlace).OfType<GeocodedPlace>()
                        .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Name));
                    if (place is not null) return place with { Kind = "stop" };
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException
                                               or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                           && !cancellationToken.IsCancellationRequested)
                {
                    logger.LogWarning("Photon station lookup failed ({Error})", ex.GetType().Name);
                    entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
                    return null;
                }
            }
            return null;
        });
    }
}
