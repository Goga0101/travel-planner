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
    string? MapsEmbedApiKey);

[ApiController]
[Route(ApiConstants.RoutePrefix + "/client-config")]
[Produces("application/json")]
public sealed class ClientConfigController(ProviderStatusRegistry providers, IDataModeAccessor dataMode, IConfiguration configuration) : ControllerBase
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
        configuration["ClientConfig:MapsEmbedApiKey"] is { Length: > 0 } key && !key.StartsWith('<') ? key : null);
}
