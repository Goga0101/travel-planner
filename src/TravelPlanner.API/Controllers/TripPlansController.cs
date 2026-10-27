using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Planning;

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

    /// <summary>
    /// Indicative hotel prices per night for the plan's dates (Booking.com and other agencies, via a price-comparison feed).
    /// 204 when no estimates are available. Never added to the plan total.
    /// </summary>
    [HttpGet("{id:guid}/hotel-prices")]
    [ProducesResponseType<HotelPricesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HotelPricesDto>> GetHotelPrices(Guid id, CancellationToken cancellationToken) =>
        await sender.Send(new GetHotelPricesQuery(id), cancellationToken) is { } prices ? prices : NoContent();

    /// <summary>
    /// A day-by-day itinerary for the chosen interests (comma separated: culture, museums, food, cafes, bars, nightlife, nature,
    /// shopping, shows) and pace (Relaxed, Normal, Intense), with alternatives per slot and the city's events during the trip.
    /// </summary>
    [HttpGet("{id:guid}/day-plan")]
    [ProducesResponseType<DayPlanDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<DayPlanDto> GetDayPlan(Guid id, [FromQuery] string? interests, [FromQuery] TripPace pace = TripPace.Normal,
        [FromQuery] string? music = null, CancellationToken cancellationToken = default) =>
        sender.Send(new GetDayPlanQuery(id,
            (interests ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), pace,
            (music ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), cancellationToken);
}
