using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application.Features.Journeys;

namespace TravelPlanner.API.Controllers;

public sealed record JourneyDayRequest(bool Done);

/// <summary>
/// Booked trips of the signed-in traveller: "I booked this plan", then ticking off each day of the trip. A trip with every
/// day ticked is completed and counts on the leaderboard.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiConstants.RoutePrefix + "/journeys")]
[Produces("application/json")]
public sealed class JourneysController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<JourneyDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<JourneyDto>>> Mine(CancellationToken cancellationToken) =>
        UserId() is { } id ? Ok(await sender.Send(new GetMyJourneysQuery(id), cancellationToken)) : Unauthorized();

    /// <summary>"I booked this trip": starts tracking the plan's days. Idempotent.</summary>
    [HttpPut("{planId:guid}")]
    [EnableRateLimiting(ApiConstants.RefreshPolicy)]
    [ProducesResponseType<JourneyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JourneyDto>> Start(Guid planId, CancellationToken cancellationToken) =>
        UserId() is { } id ? Ok(await sender.Send(new StartJourneyCommand(id, planId), cancellationToken)) : Unauthorized();

    /// <summary>Stops tracking the trip and forgets its ticked days.</summary>
    [HttpDelete("{planId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Cancel(Guid planId, CancellationToken cancellationToken)
    {
        if (UserId() is not { } id) return Unauthorized();
        await sender.Send(new CancelJourneyCommand(id, planId), cancellationToken);
        return NoContent();
    }

    /// <summary>Ticks or unticks one day (yyyy-MM-dd). Days that have not begun yet are refused (400).</summary>
    [HttpPut("{planId:guid}/days/{day}")]
    [EnableRateLimiting(ApiConstants.RefreshPolicy)]
    [ProducesResponseType<JourneyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JourneyDto>> SetDay(Guid planId, DateOnly day, [FromBody] JourneyDayRequest request, CancellationToken cancellationToken) =>
        UserId() is { } id ? Ok(await sender.Send(new SetJourneyDayCommand(id, planId, day, request.Done), cancellationToken)) : Unauthorized();

    private Guid? UserId() => Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
}

/// <summary>Travellers who completed trips booked through Tripora. Public; the signed-in traveller sees their own place.</summary>
[ApiController]
[AllowAnonymous]
[Route(ApiConstants.RoutePrefix + "/leaderboard")]
[Produces("application/json")]
public sealed class LeaderboardController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<LeaderboardDto>(StatusCodes.Status200OK)]
    public Task<LeaderboardDto> Get([FromQuery] int top = 10, CancellationToken cancellationToken = default)
    {
        Guid? me = Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
        return sender.Send(new GetLeaderboardQuery(me, top), cancellationToken);
    }
}
