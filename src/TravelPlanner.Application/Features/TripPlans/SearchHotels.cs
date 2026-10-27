using FluentValidation;
using MediatR;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.TripPlans;

/// <summary>
/// The visitor already found a hotel elsewhere: look it up by (part of) its name around the destination so it can be
/// chosen as the stay; routes and the day plan then start there.
/// </summary>
public sealed record SearchHotelsQuery(Guid PlanId, string Text) : IRequest<IReadOnlyList<HotelSearchResultDto>>;

public sealed record HotelSearchResultDto(
    string Id, string Name, string? Address, string Kind, double Latitude, double Longitude, int DistanceFromCentreMeters,
    string? BookingUrl, string? MapUrl);

public sealed class SearchHotelsValidator : AbstractValidator<SearchHotelsQuery>
{
    public SearchHotelsValidator()
    {
        RuleFor(x => x.Text).NotEmpty().Must(t => t.Trim().Length >= 2).WithMessage("Type at least 2 characters.").MaximumLength(100);
    }
}

public sealed class SearchHotelsHandler(
    IApplicationDbContext db,
    IHotelSearch search,
    IAccommodationSearchLinkBuilder bookingLinks,
    IOutboundLinkPolicy linkPolicy) : IRequestHandler<SearchHotelsQuery, IReadOnlyList<HotelSearchResultDto>>
{
    /// <summary>Hotels of the trip's city and its surroundings (airport hotels, the coast…).</summary>
    public const int RadiusMeters = 40_000;
    private const int MaxResults = 8;

    public async Task<IReadOnlyList<HotelSearchResultDto>> Handle(SearchHotelsQuery request, CancellationToken cancellationToken)
    {
        var plan = await TripPlanLoader.LoadAsync(db, request.PlanId, tracking: false, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.PlanId);
        var destination = plan.Destination!;
        var rooms = Math.Max(1, (plan.Travelers + 1) / 2);
        var hits = await search.SearchAsync(
            new HotelSearchQuery(request.Text.Trim(), destination.Latitude, destination.Longitude, RadiusMeters), cancellationToken);

        return hits
            .Select(h => (h, d: NearbyLinks.DistanceMeters(destination.Latitude, destination.Longitude, h.Latitude, h.Longitude)))
            .Where(x => x.d <= RadiusMeters)
            .Take(MaxResults)
            .Select(x => new HotelSearchResultDto(x.h.Id, x.h.Name, x.h.Address, x.h.Kind, x.h.Latitude, x.h.Longitude, x.d,
                linkPolicy.Sanitize(bookingLinks.Build(x.h.Name, destination.City, plan.DepartureDate, plan.ReturnDate, plan.Travelers, rooms)?.Url),
                linkPolicy.Sanitize(NearbyLinks.MapSearch($"{x.h.Name}, {destination.City}"))))
            .ToList();
    }
}
