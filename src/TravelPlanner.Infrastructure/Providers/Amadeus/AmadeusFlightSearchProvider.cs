using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Providers.Amadeus;

/// <summary>OAuth2 client-credentials token cache for Amadeus.</summary>
public sealed class AmadeusTokenProvider(IHttpClientFactory httpClientFactory, IOptions<AmadeusOptions> options, TimeProvider timeProvider)
{
    public const string HttpClientName = "amadeus-auth";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && timeProvider.GetUtcNow() < _expiresAt) return _token;
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && timeProvider.GetUtcNow() < _expiresAt) return _token;

            var o = options.Value;
            var client = httpClientFactory.CreateClient(HttpClientName);
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = o.ClientId!,
                ["client_secret"] = o.ClientSecret!
            });
            using var response = await client.PostAsync("v1/security/oauth2/token", content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var token = await response.Content.ReadFromJsonAsync<AmadeusTokenResponse>(cancellationToken);
            if (string.IsNullOrEmpty(token?.AccessToken)) return null;
            _token = token.AccessToken;
            _expiresAt = timeProvider.GetUtcNow().AddSeconds(Math.Max(60, token.ExpiresIn - 60));
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate() => _token = null;
}

/// <summary>
/// Amadeus Flight Offers Search (GET /v2/shopping/flight-offers). Requires Amadeus Enterprise credentials:
/// the Self-Service programme was shut down on 2026-07-17.
/// </summary>
public sealed class AmadeusFlightSearchProvider(
    HttpClient httpClient,
    AmadeusTokenProvider tokenProvider,
    IOptions<AmadeusOptions> options,
    TimeProvider timeProvider,
    ILogger<AmadeusFlightSearchProvider> logger) : IFlightSearchProvider
{
    private readonly AmadeusFlightOfferMapper _mapper = new(options.Value.OfferValidityMinutes);

    public string Name => AmadeusFlightOfferMapper.ProviderName;
    public bool IsDemo => false;

    public Task<ProviderResult<IReadOnlyList<FlightOffer>>> SearchRoundTripAsync(FlightSearchRequest request, CancellationToken cancellationToken) =>
        ProviderHttp.Guard(Name, async () =>
        {
            var token = await tokenProvider.GetTokenAsync(cancellationToken);
            if (token is null)
                return ProviderResult<IReadOnlyList<FlightOffer>>.Failure(Name, Application.Providers.ProviderErrorKind.NotConfigured, "Authentication with Amadeus failed.");

            var query = new Dictionary<string, string>
            {
                ["originLocationCode"] = request.OriginIata,
                ["destinationLocationCode"] = request.DestinationIata,
                ["departureDate"] = ProviderHttp.Iso(request.DepartureDate),
                ["returnDate"] = ProviderHttp.Iso(request.ReturnDate),
                ["adults"] = request.Adults.ToString(),
                ["currencyCode"] = request.Currency,
                ["max"] = Math.Clamp(request.MaxResults, 1, 250).ToString()
            };
            if (request.MaxStops == 0) query["nonStop"] = "true";
            if (request.RequireCheckedBag) query["includedCheckedBagsOnly"] = "true";

            var url = "v2/shopping/flight-offers?" + string.Join("&", query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) tokenProvider.Invalidate();
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Amadeus flight search {Origin}->{Destination} returned {Status}", request.OriginIata, request.DestinationIata, (int)response.StatusCode);
                return ProviderResult<IReadOnlyList<FlightOffer>>.Failure(Name, ProviderHttp.Classify(response.StatusCode), $"Amadeus returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<AmadeusFlightOffersResponse>(cancellationToken) ?? new AmadeusFlightOffersResponse();
            return ProviderResult<IReadOnlyList<FlightOffer>>.Success(Name, _mapper.Map(body, request, timeProvider.GetUtcNow()));
        }, cancellationToken);
}
