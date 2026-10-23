using System.Net;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Infrastructure.Providers;

public sealed class ProvidersOptions
{
    public const string SectionName = "Providers";

    /// <summary>"Demo" registers only the clearly labelled demo providers; "Live" registers configured real providers only.</summary>
    public DataMode Mode { get; set; } = DataMode.Demo;
    public int FlightCacheMinutes { get; set; } = 10;
    /// <summary>Keep short: accommodation partners restrict how long availability/prices may be cached.</summary>
    public int AccommodationCacheMinutes { get; set; } = 10;
    public int TransportCacheMinutes { get; set; } = 60;
    public HttpResilienceSettings Http { get; set; } = new();
}

public sealed class HttpResilienceSettings
{
    public int AttemptTimeoutSeconds { get; set; } = 10;
    public int TotalTimeoutSeconds { get; set; } = 25;
    public int MaxRetryAttempts { get; set; } = 2;
}

public sealed class DataModeAccessor(DataMode mode) : IDataModeAccessor
{
    public DataMode Mode { get; } = mode;
}

/// <summary>Startup snapshot of which providers are active, exposed to the UI (no secrets). Texts are localization keys.</summary>
public sealed record ProviderStatus(string Category, string Name, bool Active, bool IsDemo, string NoteKey);

public sealed class ProviderStatusRegistry
{
    private readonly List<ProviderStatus> _statuses = [];
    public IReadOnlyList<ProviderStatus> Statuses => _statuses;
    public void Add(ProviderStatus status) => _statuses.Add(status);
}

internal static class ProviderHttp
{
    public static ProviderErrorKind Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => ProviderErrorKind.RateLimited,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderErrorKind.NotConfigured,
        HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity => ProviderErrorKind.InvalidRequest,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => ProviderErrorKind.Timeout,
        _ => ProviderErrorKind.Unavailable
    };

    /// <summary>Executes a provider HTTP call and converts transport failures into provider results (no exceptions, no secrets in messages).</summary>
    public static async Task<ProviderResult<T>> Guard<T>(string provider, Func<Task<ProviderResult<T>>> call, CancellationToken cancellationToken)
    {
        try
        {
            return await call();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ProviderResult<T>.Failure(provider, ProviderErrorKind.Timeout, "The provider did not respond in time.");
        }
        catch (Polly.Timeout.TimeoutRejectedException)
        {
            return ProviderResult<T>.Failure(provider, ProviderErrorKind.Timeout, "The provider did not respond in time.");
        }
        catch (Polly.CircuitBreaker.BrokenCircuitException)
        {
            return ProviderResult<T>.Failure(provider, ProviderErrorKind.Unavailable, "The provider is temporarily unavailable.");
        }
        catch (HttpRequestException)
        {
            return ProviderResult<T>.Failure(provider, ProviderErrorKind.Unavailable, "The provider could not be reached.");
        }
        catch (System.Text.Json.JsonException)
        {
            return ProviderResult<T>.Failure(provider, ProviderErrorKind.InvalidResponse, "The provider returned an unexpected response.");
        }
    }

    public static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    public static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("<", StringComparison.Ordinal);

    public static readonly string[] Supported = BudgetOptions.SupportedCurrencies;
}
