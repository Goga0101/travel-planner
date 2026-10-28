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
        if (TownPick.Parse(cityOrAirportCode) is { } town) return await ResolveTownAsync(town, cancellationToken);
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

    /// <summary>
    /// A town without an airport becomes a non-catalog destination ("town-ge-41.62-45.92") at the town itself, reached through
    /// its nearest airports (gateways); null when no airport is within reach.
    /// </summary>
    private async Task<Destination?> ResolveTownAsync(TownPick town, CancellationToken ct)
    {
        var gateways = await GatewayAirports.FindAsync(db, town.Latitude, town.Longitude, town.CountryCode, ct);
        if (gateways.Count == 0) return null;

        var code = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"town-{town.CountryCode.ToLowerInvariant()}-{town.Latitude:0.00}-{town.Longitude:0.00}");
        var destination = db.Destinations.Local.FirstOrDefault(d => d.Code == code)
                          ?? await db.Destinations.FirstOrDefaultAsync(d => d.Code == code, ct);
        if (destination is null)
        {
            var gateway = gateways[0].Airport;
            var country = world.FindCity(gateway.IataCode) is { } c && c.CountryCode == town.CountryCode ? c.Country : gateway.Country ?? town.CountryCode;
            destination = new Destination(code, town.Name, town.CountryCode, country, town.Latitude, town.Longitude)
            {
                Region = world.FindCity(gateway.IataCode)?.Continent ?? "Europe",
                IsCatalog = false,
                Popularity = 0
            };
            db.Destinations.Add(destination);
        }
        destination.IsActive = true;
        destination.GatewayAirports = gateways.Select(g => g.Airport.IataCode).ToList();
        // The names as the visitor saw them ("სიღნაღი, საქართველო"); the English base country comes from the airport list.
        if (town.Country is { } countryName)
            destination.Translations = new Dictionary<string, Domain.Entities.DestinationTranslation>(destination.Translations)
            {
                [T.CurrentLanguage] = new() { City = town.Name, Country = countryName }
            };
        return destination;
    }
}
