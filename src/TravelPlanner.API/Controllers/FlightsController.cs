using MediatR;
using Microsoft.AspNetCore.Mvc;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application.Features.Flights;

namespace TravelPlanner.API.Controllers;

[ApiController]
[Route(ApiConstants.RoutePrefix + "/flights")]
[Produces("application/json")]
public sealed class FlightsController(ISender sender) : ControllerBase
{
    /// <summary>
    /// The cheapest known one-way fare of each day over the coming months between two cities or airports, without fixed
    /// dates, plus the same for nearby airports that have fares. Cached price indications, per passenger.
    /// </summary>
    [HttpGet("calendar")]
    [ProducesResponseType<FlightCalendarDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<FlightCalendarDto> Calendar([FromQuery] string origin, [FromQuery] string destination, [FromQuery] string currency = "EUR",
        [FromQuery] int months = 12, [FromQuery] bool nearby = true, CancellationToken cancellationToken = default) =>
        sender.Send(new GetFlightCalendarQuery(origin, destination, currency, months, nearby), cancellationToken);

    /// <summary>
    /// The flights for chosen dates, cheapest first: one way without a return date, otherwise round-trip tickets and
    /// combinations of separate one-way tickets, each with its booking link(s). Prices are for all travellers.
    /// </summary>
    [HttpGet("options")]
    [ProducesResponseType<FlightOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<FlightOptionsDto> Options([FromQuery] string origin, [FromQuery] string destination, [FromQuery] DateOnly departure,
        [FromQuery(Name = "return")] DateOnly? returnDate, [FromQuery] int adults = 1, [FromQuery] string currency = "EUR",
        CancellationToken cancellationToken = default) =>
        sender.Send(new GetFlightOptionsQuery(origin, destination, departure, returnDate, adults, currency), cancellationToken);
}
