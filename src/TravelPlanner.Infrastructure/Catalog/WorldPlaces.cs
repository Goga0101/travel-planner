using TravelPlanner.Application.Providers;

namespace TravelPlanner.Infrastructure.Catalog;

/// <summary>In-memory city index over the world airport snapshot.</summary>
public sealed class WorldPlaces : IWorldPlaces
{
    private readonly Lazy<Index> _index;

    public WorldPlaces(WorldAirportsFileReader reader) => _index = new Lazy<Index>(() => Build(reader.Airports));

    public WorldCity? FindCity(string cityOrAirportCode)
    {
        var code = cityOrAirportCode.Trim().ToUpperInvariant();
        var index = _index.Value;
        if (index.Cities.TryGetValue(code, out var city)) return city;
        return index.CityOfAirport.TryGetValue(code, out var cityCode) ? index.Cities[cityCode] : null;
    }

    public IReadOnlyList<WorldCity> SearchCities(string term, int max)
    {
        var t = term.Trim();
        if (t.Length == 0) return [];
        var index = _index.Value;

        int Rank(WorldCity c)
        {
            if (c.CityCode.Equals(t, StringComparison.OrdinalIgnoreCase) || c.AirportCodes.Contains(t, StringComparer.OrdinalIgnoreCase)) return 0;
            if (c.City.Equals(t, StringComparison.OrdinalIgnoreCase)) return 1;
            if (c.City.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return 2;
            if (c.City.Contains(t, StringComparison.OrdinalIgnoreCase)) return 3;
            if (c.Country.StartsWith(t, StringComparison.OrdinalIgnoreCase)) return 4;
            return int.MaxValue;
        }

        // Within a rank, cities with more airports (usually the bigger hubs) come first.
        return index.Cities.Values
            .Select(c => (City: c, Rank: Rank(c)))
            .Where(x => x.Rank != int.MaxValue)
            .OrderBy(x => x.Rank).ThenByDescending(x => x.City.AirportCodes.Count).ThenBy(x => x.City.City)
            .Take(max)
            .Select(x => x.City)
            .ToList();
    }

    public IReadOnlyList<(WorldCity City, double Km)> CitiesNear(double latitude, double longitude, double maxKm, int max) =>
        _index.Value.Cities.Values
            .Select(c => (City: c, Km: DistanceKm(latitude, longitude, c.Latitude, c.Longitude)))
            .Where(x => x.Km <= maxKm)
            .OrderBy(x => x.Km)
            .Take(max)
            .ToList();

    private static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2) +
                Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return 2 * 6371 * Math.Asin(Math.Sqrt(a));
    }

    private sealed record Index(Dictionary<string, WorldCity> Cities, Dictionary<string, string> CityOfAirport);

    private static Index Build(IReadOnlyList<WorldAirport> airports)
    {
        var cities = airports
            .GroupBy(a => a.CityCode.ToUpperInvariant())
            .ToDictionary(g => g.Key, g =>
            {
                var first = g.First();
                return new WorldCity(g.Key, first.City, first.CountryCode, first.Country, first.Continent, first.CityLatitude, first.CityLongitude,
                    g.Select(a => a.Iata).Order().ToList(), g.Select(a => a.TimeZone).FirstOrDefault(z => z is not null));
            });
        var cityOfAirport = airports.ToDictionary(a => a.Iata.ToUpperInvariant(), a => a.CityCode.ToUpperInvariant());
        return new Index(cities, cityOfAirport);
    }
}
