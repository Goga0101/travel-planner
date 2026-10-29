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
        [FromQuery] int months = 12, CancellationToken cancellationToken = default) =>
        sender.Send(new GetFlightCalendarQuery(origin, destination, currency, months), cancellationToken);
}
