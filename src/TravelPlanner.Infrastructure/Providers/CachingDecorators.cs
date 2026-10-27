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

/// <summary>
/// Caches exact-date, whole-window and (when the inner provider supports them) self-transfer searches of a
/// window-capable flight provider.
/// </summary>
public sealed class CachingFlightWindowSearchProvider(IFlightWindowSearchProvider inner, IMemoryCache cache, TimeSpan ttl)
    : IFlightWindowSearchProvider, ISelfTransferFlightProvider
{
    private readonly CachingFlightSearchProvider _exact = new(inner, cache, ttl);

    public string Name => inner.Name;
    public bool IsDemo => inner.IsDemo;

    public Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken) =>
        _exact.SearchRoundTripAsync(request, cancellationToken);

    public async Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchWindowAsync(FlightWindowRequest request, CancellationToken cancellationToken)
    {
        var key = $"flight-window|{Name}|{request}";
        if (cache.TryGetValue(key, out ProviderResult<IReadOnlyList<FlightOffer>>? cached) && cached is not null) return cached;
        var result = await inner.SearchWindowAsync(request, cancellationToken);
        if (result.Succeeded && ttl > TimeSpan.Zero) cache.Set(key, result, ttl);
        return result;
    }

    public async Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchSelfTransferAsync(FlightWindowRequest request, CancellationToken cancellationToken)
    {
        if (inner is not ISelfTransferFlightProvider selfTransfer) return ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name, []);
        var key = $"flight-self-transfer|{Name}|{request}";
        if (cache.TryGetValue(key, out ProviderResult<IReadOnlyList<FlightOffer>>? cached) && cached is not null) return cached;
        var result = await selfTransfer.SearchSelfTransferAsync(request, cancellationToken);
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
