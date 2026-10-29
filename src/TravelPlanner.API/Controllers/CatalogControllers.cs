using System.Net;
using System.Net.Sockets;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Features.Destinations;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Domain.ValueObjects;
using TravelPlanner.Infrastructure.Providers;

namespace TravelPlanner.API.Controllers;

[ApiController]
[Route(ApiConstants.RoutePrefix + "/destinations")]
[Produces("application/json")]
public sealed class DestinationsController(ISender sender) : ControllerBase
{
    /// <summary>Active catalog destinations (paged, searchable by city, country or IATA code).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<DestinationSummaryDto>>(StatusCodes.Status200OK)]
    public Task<PagedResult<DestinationSummaryDto>> List([FromQuery] string? search, [FromQuery] string? countryCode,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        sender.Send(new GetDestinationsQuery(search, countryCode, page, pageSize), cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<DestinationDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<DestinationDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetDestinationByIdQuery(id), cancellationToken);

    /// <summary>
    /// The city's best-known sights (OpenStreetMap + Wikidata ranking + Wikipedia summary in the request language) with the
    /// metro/bus stops next to each. Empty when the places provider is not configured.
    /// </summary>
    [HttpGet("{id:guid}/attractions")]
    [ProducesResponseType<IReadOnlyList<AttractionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<AttractionDto>> GetAttractions(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetAttractionsQuery(id), cancellationToken);
}

[ApiController]
[Route(ApiConstants.RoutePrefix + "/airports")]
[Produces("application/json")]
public sealed class AirportsController(ISender sender) : ControllerBase
{
    /// <summary>Airports; use departureOnly=true for the departure selector.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AirportDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AirportDto>> List([FromQuery] bool departureOnly = true, [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetAirportsQuery(departureOnly, search), cancellationToken);

    /// <summary>
    /// The departure airport nearest to the caller, located by IP address (Travelpayouts "whereami"). 204 when unknown.
    /// Behind the Cloudflare tunnel the visitor's address comes from CF-Connecting-IP; local and private addresses are not
    /// sent, so the service then sees this server's own public address (the developer's location).
    /// </summary>
    [HttpGet("nearest")]
    [ProducesResponseType<AirportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Nearest(CancellationToken cancellationToken)
    {
        var airport = await sender.Send(new GetNearestAirportQuery(VisitorAddress()), cancellationToken);
        return airport is null ? NoContent() : Ok(airport);
    }

    private string? VisitorAddress()
    {
        var header = Request.Headers["CF-Connecting-IP"].FirstOrDefault()
                     ?? Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim();
        var address = IPAddress.TryParse(header, out var parsed) ? parsed : HttpContext.Connection.RemoteIpAddress;
        return address is null || IsLocal(address) ? null : address.ToString();
    }

    private static bool IsLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) ||
               (b[0] == 100 && b[1] is >= 64 and <= 127);
    }
}

[ApiController]
[Route(ApiConstants.RoutePrefix + "/places")]
[Produces("application/json")]
public sealed class PlacesController(ISender sender) : ControllerBase
{
    /// <summary>Cities anywhere in the world with a flightable airport, for picking destinations outside the catalog.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlaceDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PlaceDto>> Search([FromQuery] string search, [FromQuery] int max = 15, CancellationToken cancellationToken = default) =>
        sender.Send(new SearchPlacesQuery(search, max), cancellationToken);
}

public sealed record ProviderStatusDto(string Category, string Name, bool Active, bool IsDemo, string Note);

public sealed record ClientConfigDto(
    DataMode DataMode,
    IReadOnlyList<ProviderStatusDto> Providers,
    IReadOnlyList<string> SupportedCurrencies,
    int MinDurationDays,
    int MaxDurationDays,
    int MaxTravelers,
    int MaxSearchWindowDays,
    int MaxManualDestinations,
    string? MapsEmbedApiKey,
    // Public ids for the sign-in buttons; null = that provider is off.
    string? GoogleClientId = null,
    string? FacebookAppId = null);

[ApiController]
[Route(ApiConstants.RoutePrefix + "/client-config")]
[Produces("application/json")]
public sealed class ClientConfigController(ProviderStatusRegistry providers, IDataModeAccessor dataMode, IConfiguration configuration,
    IEnumerable<Application.Features.Auth.IExternalIdentityVerifier> signIn, Application.Features.Auth.IAccessTokenIssuer tokens) : ControllerBase
{
    /// <summary>Non-secret settings for the web client: data mode, active providers and input limits.</summary>
    [HttpGet]
    [ProducesResponseType<ClientConfigDto>(StatusCodes.Status200OK)]
    public ClientConfigDto Get() => new(
        dataMode.Mode,
        providers.Statuses.Select(s => new ProviderStatusDto(T.Get("area." + s.Category), s.IsDemo ? T.Get("status.demoProvider") : s.Name,
            s.Active, s.IsDemo, T.Get(s.NoteKey))).ToList(),
        BudgetOptions.SupportedCurrencies,
        TravelDates.MinimumDurationDays,
        TravelDates.MaximumDurationDays,
        9,
        Application.Features.TripSearches.CreateTripSearchValidator.MaxSearchWindowDays,
        Application.Features.TripSearches.CreateTripSearchValidator.MaxManualDestinations,
        // Browser key for the Maps Embed API: must be HTTP-referrer restricted in Google Cloud (it is public by design).
        configuration["ClientConfig:MapsEmbedApiKey"] is { Length: > 0 } key && !key.StartsWith('<') ? key : null,
        SignInId(Domain.Entities.AuthProviders.Google),
        SignInId(Domain.Entities.AuthProviders.Facebook));

    private string? SignInId(string provider) =>
        tokens.IsConfigured ? signIn.FirstOrDefault(v => v.Provider == provider && v.IsConfigured)?.PublicClientId : null;
}
