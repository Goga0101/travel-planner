using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.ExchangeRates;

public sealed class ExchangeRateOptions
{
    public const string SectionName = "ExchangeRates";

    public bool Enabled { get; set; } = true;
    /// <summary>European Central Bank euro foreign exchange reference rates (free, no key).</summary>
    public string EcbDailyUrl { get; set; } = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";
    public int CacheHours { get; set; } = 6;
}

public sealed record EcbRates(DateOnly Date, IReadOnlyDictionary<string, decimal> PerEuro);

/// <summary>
/// Uses ECB reference rates (EUR base, cross rates derived via EUR). Returns null when a currency is not published
/// or the feed is unreachable, so callers never sum currencies without a recorded rate.
/// </summary>
public sealed class EcbExchangeRateProvider(
    HttpClient httpClient,
    IMemoryCache cache,
    IOptions<ExchangeRateOptions> options,
    ILogger<EcbExchangeRateProvider> logger) : IExchangeRateProvider
{
    private const string CacheKey = "ecb-daily-rates";
    public const string SourceName = "ECB euro reference rate";

    public async Task<ExchangeRate?> GetRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken)
    {
        var from = fromCurrency.ToUpperInvariant();
        var to = toCurrency.ToUpperInvariant();
        if (!options.Value.Enabled) return null;

        var rates = await GetRatesAsync(cancellationToken);
        if (rates is null) return null;
        return Cross(rates, from, to);
    }

    public static ExchangeRate? Cross(EcbRates rates, string from, string to)
    {
        decimal? PerEuro(string c) => c == "EUR" ? 1m : rates.PerEuro.TryGetValue(c, out var r) ? r : null;
        var fromRate = PerEuro(from);
        var toRate = PerEuro(to);
        if (fromRate is not > 0 || toRate is not > 0) return null;

        var asOf = new DateTimeOffset(rates.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        return new ExchangeRate(from, to, Math.Round(toRate.Value / fromRate.Value, 8), $"{SourceName} ({rates.Date:yyyy-MM-dd})", asOf);
    }

    public static EcbRates Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var dated = doc.Descendants().First(e => e.Name.LocalName == "Cube" && e.Attribute("time") is not null);
        var date = DateOnly.ParseExact(dated.Attribute("time")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var perEuro = dated.Elements()
            .Where(e => e.Attribute("currency") is not null && e.Attribute("rate") is not null)
            .ToDictionary(e => e.Attribute("currency")!.Value.ToUpperInvariant(),
                e => decimal.Parse(e.Attribute("rate")!.Value, CultureInfo.InvariantCulture));
        return new EcbRates(date, perEuro);
    }

    private async Task<EcbRates?> GetRatesAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out EcbRates? cached)) return cached;
        try
        {
            var xml = await httpClient.GetStringAsync(options.Value.EcbDailyUrl, cancellationToken);
            var rates = Parse(xml);
            cache.Set(CacheKey, rates, TimeSpan.FromHours(Math.Max(1, options.Value.CacheHours)));
            return rates;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or FormatException or InvalidOperationException
                                       or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Could not load ECB exchange rates; currency conversion is unavailable");
            return null;
        }
    }
}
