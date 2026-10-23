using Microsoft.Extensions.Caching.Memory;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers;

// In-memory caches for successful provider responses. Cached offers keep their original RetrievedAtUtc/ExpiresAtUtc,
// so the UI always shows when a price was really retrieved. TTLs are configurable and kept short.

public sealed class CachingFlightSearchProvider(IFlightSearchProvider inner, IMemoryCache cache, TimeSpan ttl) : IFlightSearchProvider
{
    public string Name => inner.Name;
    public bool IsDemo => inner.IsDemo;

    public async Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken)
    {
        var key = $"flights|{Name}|{request}";
        if (cache.TryGetValue(key, out ProviderResult<IReadOnlyList<FlightOffer>>? cached) && cached is not null) return cached;
        var result = await inner.SearchRoundTripAsync(request, cancellationToken);
        if (result.Succeeded && ttl > TimeSpan.Zero) cache.Set(key, result, ttl);
        return result;
    }
}

public sealed class CachingAccommodationProvider(IAccommodationSearchProvider inner, IMemoryCache cache, TimeSpan ttl) : IAccommodationSearchProvider
{
    public string Name => inner.Name;
    public bool IsDemo => inner.IsDemo;

    public async Task<ProviderResult<IReadOnlyList<AccommodationOffer>>> SearchAsync(AccommodationSearchRequest request, CancellationToken cancellationToken)
    {
        var key = $"stays|{Name}|{request.CacheKey}|{request.MaxResults}";
        if (cache.TryGetValue(key, out ProviderResult<IReadOnlyList<AccommodationOffer>>? cached) && cached is not null) return cached;
        var result = await inner.SearchAsync(request, cancellationToken);
        if (result.Succeeded && ttl > TimeSpan.Zero) cache.Set(key, result, ttl);
        return result;
    }
}

public sealed class CachingTransportProvider(ITransportSearchProvider inner, IMemoryCache cache, TimeSpan ttl) : ITransportSearchProvider
{
    public string Name => inner.Name;
    public bool IsDemo => inner.IsDemo;

    public async Task<ProviderResult<IReadOnlyList<TransferOption>>> SearchAsync(TransportSearchRequest request, CancellationToken cancellationToken)
    {
        var key = $"transport|{Name}|{request.CacheKey}";
        if (cache.TryGetValue(key, out ProviderResult<IReadOnlyList<TransferOption>>? cached) && cached is not null) return cached;
        var result = await inner.SearchAsync(request, cancellationToken);
        if (result.Succeeded && ttl > TimeSpan.Zero) cache.Set(key, result, ttl);
        return result;
    }
}
