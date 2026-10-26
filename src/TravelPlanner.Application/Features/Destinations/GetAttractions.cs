using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.Destinations;

public sealed record GetAttractionsQuery(Guid DestinationId) : IRequest<IReadOnlyList<AttractionDto>>;

public sealed record AttractionStopDto(string Name, string Mode, IReadOnlyList<string> Lines, int DistanceMeters);

/// <summary>
/// A well-known sight. WikipediaEditions is how many language editions describe it (the ranking signal).
/// DirectionsUrl opens Google Maps public-transport directions from the visitor's current location.
/// </summary>
public sealed record AttractionDto(
    string Id,
    string Name,
    string Kind,
    double Latitude,
    double Longitude,
    int DistanceFromCentreMeters,
    int WikipediaEditions,
    string? ShortDescription,
    string? Summary,
    string? SummaryLanguage,
    string? ImageUrl,
    string? WikipediaUrl,
    string? Website,
    string? OpeningHours,
    string? Fee,
    IReadOnlyList<AttractionStopDto> Stops,
    string? MapUrl,
    string? DirectionsUrl);

public sealed class GetAttractionsHandler(IApplicationDbContext db, IAttractionSource source, IOutboundLinkPolicy linkPolicy)
    : IRequestHandler<GetAttractionsQuery, IReadOnlyList<AttractionDto>>
{
    public async Task<IReadOnlyList<AttractionDto>> Handle(GetAttractionsQuery request, CancellationToken cancellationToken)
    {
        var d = await db.Destinations.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.DestinationId, cancellationToken)
                ?? throw new NotFoundException("resource.destination", request.DestinationId);

        var attractions = await source.GetAsync(new AttractionQuery(d.Code, d.Latitude, d.Longitude, T.CurrentLanguage), cancellationToken);
        return attractions.Select(a => new AttractionDto(
                a.WikidataId, a.Name, a.Kind, a.Latitude, a.Longitude,
                NearbyLinks.DistanceMeters(d.Latitude, d.Longitude, a.Latitude, a.Longitude),
                a.WikipediaEditions, a.ShortDescription, a.Summary, a.SummaryLanguage,
                a.ImageUrl, linkPolicy.Sanitize(a.WikipediaUrl), a.Website, a.OpeningHours, a.Fee,
                a.Stops.Select(s => new AttractionStopDto(s.Name, s.Mode, s.Lines, s.DistanceMeters)).ToList(),
                linkPolicy.Sanitize(NearbyLinks.MapSearch($"{a.Name}, {d.City}")),
                linkPolicy.Sanitize(NearbyLinks.TransitDirectionsTo(a.Latitude, a.Longitude))))
            .ToList();
    }
}
