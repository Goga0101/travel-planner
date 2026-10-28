using MediatR;
using Microsoft.AspNetCore.Mvc;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application.Features.Transit;

namespace TravelPlanner.API.Controllers;

/// <summary>Getting around anywhere: place search, public-transport journeys, vehicles moving on the map and local taxis.</summary>
[ApiController]
[Route(ApiConstants.RoutePrefix + "/transit")]
[Produces("application/json")]
public sealed class TransitController(ISender sender) : ControllerBase
{
    /// <summary>Places by name in any country and script (addresses, streets, stops, landmarks); near=lat,lon biases the results.</summary>
    [HttpGet("places")]
    [ProducesResponseType<IReadOnlyList<PlaceSuggestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<PlaceSuggestionDto>> SearchPlaces([FromQuery] string q, [FromQuery] double? lat, [FromQuery] double? lon,
        CancellationToken cancellationToken) =>
        sender.Send(new SearchPlacesQuery(q ?? "", lat, lon), cancellationToken);

    /// <summary>The address at a point (for "my location"). 204 when unknown.</summary>
    [HttpGet("reverse")]
    [ProducesResponseType<PlaceSuggestionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<PlaceSuggestionDto>> Reverse([FromQuery] double lat, [FromQuery] double lon, CancellationToken cancellationToken) =>
        await sender.Send(new ReversePlaceQuery(lat, lon), cancellationToken) is { } place ? place : NoContent();

    /// <summary>Public-transport journeys between two points, leaving now (or at time=yyyy-MM-ddTHH:mm, local at the origin).</summary>
    [HttpGet("journey")]
    [ProducesResponseType<JourneyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<JourneyDto> Journey([FromQuery] double fromLat, [FromQuery] double fromLon, [FromQuery] double toLat, [FromQuery] double toLon,
        [FromQuery] string? fromName = null, [FromQuery] string? toName = null, [FromQuery] DateTime? time = null,
        CancellationToken cancellationToken = default) =>
        sender.Send(new PlanJourneyQuery(fromLat, fromLon, fromName, toLat, toLon, toName, time), cancellationToken);

    /// <summary>
    /// Vehicles running in the box during the next two minutes, each with its stop-to-stop stretches so the map can move it.
    /// Live where the operator publishes real-time data, otherwise on the timetable. Boxes wider than ~35 km return TooLarge.
    /// </summary>
    [HttpGet("vehicles")]
    [ProducesResponseType<LiveVehiclesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<LiveVehiclesDto> Vehicles([FromQuery] double minLat, [FromQuery] double minLon, [FromQuery] double maxLat, [FromQuery] double maxLon,
        CancellationToken cancellationToken) =>
        sender.Send(new LiveVehiclesQuery(minLat, minLon, maxLat, maxLon), cancellationToken);

    /// <summary>Taxi and ride-hailing apps used in the country, and taxi stands/companies nearby with phone numbers where known.</summary>
    [HttpGet("taxis")]
    [ProducesResponseType<TaxiOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<TaxiOptionsDto> Taxis([FromQuery] double lat, [FromQuery] double lon, [FromQuery] string? country = null,
        [FromQuery] double? toLat = null, [FromQuery] double? toLon = null, CancellationToken cancellationToken = default) =>
        sender.Send(new LocalTaxisQuery(lat, lon, country, toLat, toLon), cancellationToken);
}
