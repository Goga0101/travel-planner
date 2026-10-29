using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.JsonWebTokens;
using TravelPlanner.API.Infrastructure;
using TravelPlanner.Application.Features.Auth;

namespace TravelPlanner.API.Controllers;

public sealed record ExternalLoginRequest(string Provider, string Token);

[ApiController]
[Route(ApiConstants.RoutePrefix + "/auth")]
[Produces("application/json")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    /// <summary>
    /// Signs in with a token the browser got from Google (ID token) or Facebook (access token). The token is verified with the
    /// provider; the user is created on first sign-in. Returns this API's own bearer token for later requests.
    /// </summary>
    [HttpPost("external")]
    [EnableRateLimiting(ApiConstants.SignInPolicy)]
    [ProducesResponseType<AuthResultDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<AuthResultDto> External([FromBody] ExternalLoginRequest request, CancellationToken cancellationToken) =>
        sender.Send(new ExternalLoginCommand(request.Provider, request.Token), cancellationToken);

    /// <summary>The signed-in user.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)) return Unauthorized();
        try
        {
            return await sender.Send(new GetCurrentUserQuery(id), cancellationToken);
        }
        catch (Application.Common.NotFoundException)
        {
            return Unauthorized(); // the account no longer exists: the browser signs out
        }
    }
}
