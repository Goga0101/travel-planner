using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Features.TripPlans;

namespace TravelPlanner.API.Controllers;

[ApiController]
[Route(ApiConstants.RoutePrefix + "/trip-plans")]
[Produces("application/json")]
public sealed class TripPlansController(ISender sender) : ControllerBase
{
    /// <summary>Plan details with itemized costs, sources and verification timestamps.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<TripPlanDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TripPlanDetailDto> Get(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetTripPlanQuery(id), cancellationToken);

    /// <summary>Re-queries the providers for this plan's flight and accommodation and recalculates the budget.</summary>
    [HttpPost("{id:guid}/refresh-prices")]
    [EnableRateLimiting(ApiConstants.RefreshPolicy)]
    [ProducesResponseType<TripPlanDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TripPlanDetailDto> RefreshPrices(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new RefreshTripPlanPricesCommand(id), cancellationToken);

    /// <summary>Official, allow-listed provider links for booking and transport information.</summary>
    [HttpGet("{id:guid}/outbound-links")]
    [ProducesResponseType<IReadOnlyList<OutboundLinkDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<OutboundLinkDto>> GetOutboundLinks(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetOutboundLinksQuery(id), cancellationToken);

    /// <summary>
    /// Hotels near the arrival airport and the city centre, public-transport lines serving the airport and local taxis
    /// (OpenStreetMap snapshot), with Booking.com, Google Maps and Uber links pre-filled from the plan.
    /// </summary>
    [HttpGet("{id:guid}/nearby")]
    [ProducesResponseType<NearbyPlacesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NearbyPlacesDto> GetNearby(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetNearbyPlacesQuery(id), cancellationToken);
}
