using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Geoapify;

/// <summary>Stops near a point from Geoapify (OpenStreetMap), cached per ~100 m cell for a day.</summary>
public sealed class GeoapifyStopSource(GeoapifyPlacesClient geoapify, IMemoryCache cache, ILogger<GeoapifyStopSource> logger) : ITransitStopSource
{
    public bool IsConfigured => true;

    public async Task<IReadOnlyList<NearbyTransitStop>> FindAsync(double latitude, double longitude, int radiusMeters, CancellationToken cancellationToken)
    {
        var (lat, lon) = (Math.Round(latitude, 3), Math.Round(longitude, 3));
        try
        {
            return await cache.GetOrCreateAsync($"stops|{lat:F3},{lon:F3}|{radiusMeters}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24);
                var features = await geoapify.SearchAsync(GeoapifyMapper.StopCategories, lat, lon, radiusMeters, 60, cancellationToken);
                return (IReadOnlyList<NearbyTransitStop>)features.Select(GeoapifyMapper.ToStop).OfType<NearbyTransitStop>().ToList();
            }) ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException
                                   && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Geoapify stop lookup failed ({Error})", ex.GetType().Name);
            return [];
        }
    }
}
