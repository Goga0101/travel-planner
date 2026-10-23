namespace TravelPlanner.Application.Providers;

public enum ProviderErrorKind
{
    None = 0,
    NotConfigured,
    InvalidRequest,
    RateLimited,
    Timeout,
    Unavailable,
    InvalidResponse
}

/// <summary>Outcome of a single provider call. Failures are values, not exceptions, so one bad route never aborts a search.</summary>
public sealed record ProviderResult<T>(string Provider, T? Data, ProviderErrorKind Error, string? ErrorMessage)
{
    public bool Succeeded => Error == ProviderErrorKind.None;

    public static ProviderResult<T> Success(string provider, T data) => new(provider, data, ProviderErrorKind.None, null);

    public static ProviderResult<T> Failure(string provider, ProviderErrorKind error, string message) =>
        new(provider, default, error, message);
}
