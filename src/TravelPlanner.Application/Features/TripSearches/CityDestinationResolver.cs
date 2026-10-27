using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Application.Features.TripSearches;

/// <summary>
/// Turns a city picked from the world list into a destination: the catalog destination when the city is curated,
/// otherwise a non-catalog destination created once ("city-xxx") and reused by later searches.
/// </summary>
public sealed class CityDestinationResolver(IApplicationDbContext db, IWorldPlaces world)
{
    public async Task<Destination?> ResolveAsync(string cityOrAirportCode, CancellationToken cancellationToken)
    {
        var city = world.FindCity(cityOrAirportCode);
        if (city is null) return null;

        var airports = await db.Airports.Where(a => city.AirportCodes.Contains(a.IataCode)).ToListAsync(cancellationToken);
        if (airports.Count == 0) return null;

        var linkedId = airports.Select(a => a.DestinationId).FirstOrDefault(id => id.HasValue);
        if (linkedId is { } id && await db.Destinations.FirstOrDefaultAsync(d => d.Id == id, cancellationToken) is { } linked &&
            (linked.IsActive || !linked.IsCatalog))
        {
            linked.IsActive = true;
            return linked;
        }

        var code = "city-" + city.CityCode.ToLowerInvariant();
        var destination = db.Destinations.Local.FirstOrDefault(d => d.Code == code)
                          ?? await db.Destinations.FirstOrDefaultAsync(d => d.Code == code, cancellationToken);
        if (destination is null)
        {
            destination = new Destination(code, city.City, city.CountryCode, city.Country, city.Latitude, city.Longitude)
            {
                Region = city.Continent,
                IsCatalog = false,
                Popularity = 0
            };
            db.Destinations.Add(destination);
        }
        destination.IsActive = true;
        foreach (var airport in airports.Where(a => a.DestinationId is null || a.DestinationId != destination.Id))
            airport.DestinationId = destination.Id;
        return destination;
    }
}
