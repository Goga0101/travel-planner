using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Features.TripSearches;

namespace TravelPlanner.API.Controllers;

[ApiController]
[Route(ApiConstants.RoutePrefix + "/trip-searches")]
[Produces("application/json")]
public sealed class TripSearchesController(ISender sender) : ControllerBase
{
    /// <summary>Starts a trip search. The search runs in the background; poll the returned Location for status.</summary>
    [HttpPost]
    [EnableRateLimiting(ApiConstants.SearchCreatePolicy)]
    [ProducesResponseType<CreateTripSearchResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Create([FromBody] CreateTripSearchCommand command, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);
        return AcceptedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>Search status and progress (searching, completed, failed …).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<TripSearchDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TripSearchDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetTripSearchQuery(id), cancellationToken);

    /// <summary>Ranked plans of a search (available once the search has completed).</summary>
    [HttpGet("{id:guid}/plans")]
    [ProducesResponseType<PagedResult<TripPlanSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PagedResult<TripPlanSummaryDto>> GetPlans(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10,
        [FromQuery] PlanSort sort = PlanSort.Rank, CancellationToken cancellationToken = default) =>
        sender.Send(new GetTripSearchPlansQuery(id, page, pageSize, sort), cancellationToken);

    /// <summary>Every flight the search found across all destinations and dates, cheapest first (empty while running).</summary>
    [HttpGet("{id:guid}/flights")]
    [ProducesResponseType<IReadOnlyList<FoundFlightDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<FoundFlightDto>> GetFlights(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetFoundFlightsQuery(id), cancellationToken);

    /// <summary>Cancels a queued or running search.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<TripSearchDto>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken) =>
        Accepted(await sender.Send(new CancelTripSearchCommand(id), cancellationToken));
}
