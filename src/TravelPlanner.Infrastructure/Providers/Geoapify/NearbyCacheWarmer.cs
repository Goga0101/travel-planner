using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Geoapify;

/// <summary>
/// Loads live nearby places and sights (English and Georgian) for every active destination shortly after startup and
/// again before the caches expire, so travellers never wait for the first Geoapify/Wikimedia round trips
/// (about 20 Geoapify requests per destination per day).
/// </summary>
public sealed class NearbyCacheWarmer(IServiceScopeFactory scopes, IOptions<GeoapifyOptions> options, ILogger<NearbyCacheWarmer> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured) return;
        var interval = TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours)) - TimeSpan.FromMinutes(10);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await WarmAsync(stoppingToken);
                await Task.Delay(interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task WarmAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var source = scope.ServiceProvider.GetRequiredService<INearbyPlacesSource>();
        var attractions = scope.ServiceProvider.GetRequiredService<IAttractionSource>();
        var destinations = await db.Destinations.AsNoTracking().Include(d => d.Airports)
            .Where(d => d.IsActive).OrderByDescending(d => d.Popularity).ToListAsync(ct);

        var warmed = 0;
        foreach (var d in destinations)
        {
            try
            {
                var query = new NearbyQuery(d.Code, d.Latitude, d.Longitude,
                    d.Airports.Select(a => new NearbyAirportPoint(a.IataCode, a.Latitude, a.Longitude)).ToList());
                if (await source.FindAsync(query, ct) is not null) warmed++;
                foreach (var lang in new[] { "ka", "en" })
                    await attractions.GetAsync(new AttractionQuery(d.Code, d.Latitude, d.Longitude, lang), ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Warming nearby places for {Destination} failed ({Error})", d.Code, ex.GetType().Name);
            }
            // Stay well below Geoapify's per-second limits.
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
        logger.LogInformation("Nearby places cache warmed for {Count} of {Total} destinations", warmed, destinations.Count);
    }
}
