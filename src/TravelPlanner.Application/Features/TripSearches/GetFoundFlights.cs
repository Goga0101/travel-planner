using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Planning;

namespace TravelPlanner.Application.Features.TripSearches;

/// <summary>Every flight a finished search found, cheapest first (the client sorts and filters).</summary>
public sealed record GetFoundFlightsQuery(Guid Id) : IRequest<IReadOnlyList<FoundFlightDto>>;

public sealed class GetFoundFlightsHandler(IApplicationDbContext db) : IRequestHandler<GetFoundFlightsQuery, IReadOnlyList<FoundFlightDto>>
{
    public async Task<IReadOnlyList<FoundFlightDto>> Handle(GetFoundFlightsQuery request, CancellationToken cancellationToken)
    {
        var json = await db.TripSearches.AsNoTracking().Where(s => s.Id == request.Id).Select(s => s.FoundFlightsJson)
                       .FirstOrDefaultAsync(cancellationToken)
                   ?? throw new NotFoundException("resource.tripSearch", request.Id);

        List<FoundFlight> flights;
        try { flights = JsonSerializer.Deserialize<List<FoundFlight>>(json, AppJson.Options) ?? []; }
        catch (JsonException) { flights = []; }
        if (flights.Count == 0) return [];

        var ids = flights.Select(f => f.DestinationId).Distinct().ToList();
        var destinations = await db.Destinations.AsNoTracking().Where(d => ids.Contains(d.Id)).ToDictionaryAsync(d => d.Id, cancellationToken);
        var airports = await AirportLookup.LoadAsync(db,
            flights.SelectMany(f => AirportLookup.CodesOf(f.Outbound).Concat(AirportLookup.CodesOf(f.Inbound))), cancellationToken);

        return flights
            .Where(f => destinations.ContainsKey(f.DestinationId))
            .OrderBy(f => f.Price)
            .Select(f => f.ToDto(destinations[f.DestinationId], airports))
            .ToList();
    }
}
