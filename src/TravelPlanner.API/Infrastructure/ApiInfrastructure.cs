using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TravelPlanner.Application.Common;

namespace TravelPlanner.API.Infrastructure;

public static class ApiConstants
{
    public const string RoutePrefix = "api/v1";
    public const string CorsPolicy = "frontend";
    public const string SearchCreatePolicy = "search-create";
    public const string RefreshPolicy = "price-refresh";
    public const string SignInPolicy = "sign-in";
}

public sealed class RateLimitSettings
{
    public int GlobalPerMinute { get; set; } = 300;
    public int SearchesPerMinute { get; set; } = 10;
    public int RefreshesPerMinute { get; set; } = 20;
    public int SignInsPerMinute { get; set; } = 10;
}

/// <summary>Maps exceptions to RFC 7807 problem details. Internal details and stack traces are never returned.</summary>
public sealed class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem;
        switch (exception)
        {
            case ValidationException validation:
                var errors = validation.Errors
                    .GroupBy(e => ToCamelPath(e.PropertyName))
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
                problem = new ValidationProblemDetails(errors)
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = T.Get("api.validationTitle")
                };
                break;
            case NotFoundException:
                problem = new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = T.Get("api.notFoundTitle"), Detail = exception.Message };
                break;
            case Application.Features.Auth.AuthenticationFailedException:
                problem = new ProblemDetails { Status = StatusCodes.Status401Unauthorized, Title = T.Get("auth.failedTitle"), Detail = exception.Message };
                break;
            case ConflictException:
                problem = new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = T.Get("api.conflictTitle"), Detail = exception.Message };
                break;
            case OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested:
                httpContext.Response.StatusCode = 499; // client closed request
                return true;
            case BadHttpRequestException bad:
                problem = new ProblemDetails { Status = bad.StatusCode, Title = T.Get("api.badRequest") };
                break;
            default:
                logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
                problem = new ProblemDetails
                {
                    Status = StatusCodes.Status500InternalServerError,
                    Title = T.Get("api.unexpectedTitle"),
                    Detail = T.Get("api.tryLater")
                };
                break;
        }

        problem.Extensions["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier;
        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static string ToCamelPath(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(p => p.Length == 0 ? p : char.ToLowerInvariant(p[0]) + p[1..]));
}
