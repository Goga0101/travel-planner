using Microsoft.Extensions.Caching.Memory;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Travelpayouts;

/// <summary>Fare calendar from the same cached one-way fares the self-transfer search uses.</summary>
public sealed partial class TravelpayoutsFlightProvider : IFlightCalendarSource
{
    public bool IsAvailable => true;

    public async Task<bool> HasFaresAsync(string origin, string destination, string currency, CancellationToken cancellationToken)
    {
        var key = $"tp-any|{origin}|{destination}|{currency}";
        if (cache.TryGetValue(key, out bool known)) return known;
        await Throttle.WaitAsync(cancellationToken);
        try
        {
            var result = await FetchAsync(origin, destination, null, null, null, currency, 1, cancellationToken);
            if (!result.Succeeded) return false;
            known = result.Data!.Data.Count > 0;
            cache.Set(key, known, FareCacheTtl);
            return known;
        }
        finally
        {
            Throttle.Release();
        }
    }

    public async Task<IReadOnlyList<CalendarFare>> CheapestByDayAsync(string origin, string destination, string month, string currency,
        CancellationToken cancellationToken)
    {
        var fares = await OneWayFaresAsync(origin, destination, month, null, currency, cancellationToken);
        return fares
            .GroupBy(f => DateOnly.FromDateTime(f.Departure.DateTime))
            .Select(g => g.MinBy(f => f.Price)!)
            .OrderBy(f => f.Departure.DateTime)
            .Select(f => new CalendarFare(f.From, f.To, f.Departure.DateTime, f.Transfers, f.Airline, f.Price, currency.ToUpperInvariant(),
                _mapper.BuildLink(f.Link)))
            .ToList();
    }
}

/// <summary>Demo mode, or no Travelpayouts token: no fare calendar.</summary>
public sealed class NoFlightCalendarSource : IFlightCalendarSource
{
    public bool IsAvailable => false;

    public Task<bool> HasFaresAsync(string origin, string destination, string currency, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<CalendarFare>> CheapestByDayAsync(string origin, string destination, string month, string currency,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CalendarFare>>([]);
}
