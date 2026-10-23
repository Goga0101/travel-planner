using System.Collections.Concurrent;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Planning;

/// <summary>Per-search cache of exchange rates into the budget currency.</summary>
public sealed class CurrencyConversionContext(IExchangeRateProvider provider, string targetCurrency)
{
    private readonly ConcurrentDictionary<string, ExchangeRate?> _rates = new(StringComparer.OrdinalIgnoreCase);

    public string TargetCurrency { get; } = targetCurrency.ToUpperInvariant();

    public async Task EnsureAsync(IEnumerable<string?> currencies, CancellationToken cancellationToken)
    {
        foreach (var currency in currencies.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c!.ToUpperInvariant()).Distinct())
        {
            if (currency == TargetCurrency || _rates.ContainsKey(currency)) continue;
            _rates[currency] = await provider.GetRateAsync(currency, TargetCurrency, cancellationToken);
        }
    }

    public bool TryConvert(decimal amount, string currency, out decimal converted)
    {
        if (currency.Equals(TargetCurrency, StringComparison.OrdinalIgnoreCase))
        {
            converted = amount;
            return true;
        }
        if (_rates.TryGetValue(currency, out var rate) && rate is not null)
        {
            converted = Math.Round(amount * rate.Rate, 2, MidpointRounding.AwayFromZero);
            return true;
        }
        converted = 0;
        return false;
    }

    public IReadOnlyDictionary<string, ExchangeRate> Snapshot() =>
        _rates.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.OrdinalIgnoreCase);
}
