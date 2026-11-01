using FluentValidation;
using MediatR;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.TripPlans;

/// <summary>Guest reviews of the hotel the visitor picked for this plan, newest first.</summary>
public sealed record GetHotelReviewsQuery(Guid PlanId, string Name, double Latitude, double Longitude, string Language)
    : IRequest<HotelReviewsDto?>;

public sealed record HotelReviewDto(
    string Id, int Rating, string? Title, string Text, string Language, DateTimeOffset PublishedAt, string? TravelDate,
    string? TripType, string? Author, string? AuthorLocation, string? AvatarUrl, string? Url, string? RatingImageUrl,
    string? OwnerResponse);

/// <summary>
/// Recent = the reviews are from the last <see cref="WindowDays"/> days. When nobody reviewed the hotel in that window,
/// Recent is false and Reviews holds the latest few, so the visitor still sees something, clearly labelled.
/// Matched = false: the platform does not know this hotel.
/// </summary>
public sealed record HotelReviewsDto(
    string Source,
    bool Matched,
    string? HotelName,
    string? HotelUrl,
    int WindowDays,
    bool Recent,
    double? AverageRating,
    IReadOnlyList<HotelReviewDto> Reviews);

public sealed class GetHotelReviewsValidator : AbstractValidator<GetHotelReviewsQuery>
{
    public GetHotelReviewsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
    }
}

public sealed class GetHotelReviewsHandler(IApplicationDbContext db, IHotelReviewSource reviews, TimeProvider clock)
    : IRequestHandler<GetHotelReviewsQuery, HotelReviewsDto?>
{
    public const int WindowDays = 14;
    private const int MaxRecent = 10;
    private const int MaxFallback = 3;

    public async Task<HotelReviewsDto?> Handle(GetHotelReviewsQuery request, CancellationToken cancellationToken)
    {
        _ = await TripPlanLoader.LoadAsync(db, request.PlanId, tracking: false, cancellationToken)
            ?? throw new NotFoundException("resource.tripPlan", request.PlanId);
        if (!reviews.IsConfigured) return null;

        var result = await reviews.GetRecentAsync(
            new HotelReviewQuery(request.Name.Trim(), request.Latitude, request.Longitude, request.Language), cancellationToken);
        if (result is null) return null;
        return Shape(result, clock.GetUtcNow());
    }

    /// <summary>Keeps the last two weeks, or the latest few when there are none; newest first.</summary>
    public static HotelReviewsDto Shape(HotelReviewResult result, DateTimeOffset now)
    {
        var since = now.AddDays(-WindowDays);
        var newest = result.Reviews.OrderByDescending(r => r.PublishedAt).ToList();
        var recent = newest.Where(r => r.PublishedAt >= since).Take(MaxRecent).ToList();
        var shown = recent.Count > 0 ? recent : newest.Take(MaxFallback).ToList();
        double? average = shown.Count > 0 ? Math.Round(shown.Average(r => r.Rating), 1) : null;
        return new HotelReviewsDto(
            result.Source, result.Matched, result.HotelName, result.HotelUrl, WindowDays, recent.Count > 0, average,
            shown.Select(r => new HotelReviewDto(r.Id, r.Rating, r.Title, r.Text, r.Language, r.PublishedAt, r.TravelDate, r.TripType,
                r.Author, r.AuthorLocation, r.AvatarUrl, r.Url, r.RatingImageUrl, r.OwnerResponse)).ToList());
    }
}
