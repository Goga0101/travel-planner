using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Application.Features.Destinations;

/// <summary>Cities anywhere in the world (with a flightable airport) matching a search term.</summary>
public sealed record SearchPlacesQuery(string Search, int Max = 15) : IRequest<IReadOnlyList<PlaceDto>>;

/// <summary>The departure airport nearest to a visitor's IP address (null address = the caller as seen by the locator).</summary>
public sealed record GetNearestAirportQuery(string? IpAddress) : IRequest<AirportDto?>;

public sealed class SearchPlacesValidator : AbstractValidator<SearchPlacesQuery>
{
    public SearchPlacesValidator()
    {
        RuleFor(x => x.Search).NotEmpty().MaximumLength(100).WithMessage(_ => T.Get("val.searchLength"));
        RuleFor(x => x.Max).InclusiveBetween(1, 30);
    }
}

public sealed class SearchPlacesHandler(IApplicationDbContext db, IWorldPlaces world, ILiveTransit places)
    : IRequestHandler<SearchPlacesQuery, IReadOnlyList<PlaceDto>>
{
    /// <summary>A map place this close to a listed city is that city (a district, the same town under another name).</summary>
    private const int SameCityKm = 15;
    private const int MaxTowns = 5;

    public async Task<IReadOnlyList<PlaceDto>> Handle(SearchPlacesQuery request, CancellationToken cancellationToken)
    {
        var withAirports = await CitiesAsync(request, cancellationToken);
        var towns = withAirports.Count >= request.Max ? [] : await TownsAsync(request.Search, withAirports, cancellationToken);
        return withAirports.Concat(towns).Take(request.Max).ToList();
    }

    /// <summary>
    /// Towns and villages without an airport (Sighnaghi, Mestia), found on the map in any language, each with the airports
    /// it is reached through.
    /// </summary>
    private async Task<List<PlaceDto>> TownsAsync(string search, IReadOnlyList<PlaceDto> listed, CancellationToken ct)
    {
        var term = search.Trim();
        if (term.Length < 2) return [];
        var language = T.CurrentLanguage;
        var listedCities = listed.Select(p => world.FindCity(p.CityCode)).OfType<WorldCity>().ToList();
        var result = new List<PlaceDto>();
        // Capitals are often mapped as an administrative area ("region"): they still count when an airport city is there.
        foreach (var p in (await places.GeocodeAsync(term, language, null, null, ct)).Where(p => p.Kind is "city" or "region" && p.CountryCode is { Length: 2 }))
        {
            if (result.Count >= MaxTowns) break;
            if (listedCities.Any(c => Geo.DistanceKm(c.Latitude, c.Longitude, p.Latitude, p.Longitude) < SameCityKm)) continue;
            var gateways = await Features.TripSearches.GatewayAirports.FindAsync(db, p.Latitude, p.Longitude, p.CountryCode!, ct);
            if (gateways.Count == 0) continue;
            var gateway = gateways[0].Airport;
            var country = p.Area?.Split(',').LastOrDefault()?.Trim() is { Length: > 0 } c ? c : gateway.Country ?? p.CountryCode!;

            // An airport city typed in another language ("თბილისი"): that city itself, named as typed.
            if (world.FindCity(gateway.IataCode) is { } airportCity &&
                Geo.DistanceKm(airportCity.Latitude, airportCity.Longitude, p.Latitude, p.Longitude) < SameCityKm)
            {
                if (result.All(r => r.CityCode != airportCity.CityCode))
                    result.Add(new PlaceDto(airportCity.CityCode, p.Name, country, airportCity.CountryCode, airportCity.Continent, airportCity.AirportCodes, null));
                continue;
            }
            // A municipality or region without an airport city is an area, not a place to stay.
            if (p.Kind != "city" || result.Any(r => r.City == p.Name && r.CountryCode == p.CountryCode)) continue;
            result.Add(new PlaceDto(new Features.TripSearches.TownPick(p.Name, p.CountryCode!, p.Latitude, p.Longitude, country).Code, p.Name, country,
                p.CountryCode!, world.FindCity(gateway.IataCode)?.Continent ?? "Europe", gateways.Select(g => g.Airport.IataCode).ToList(), null,
                gateways[0].Km));
        }
        return result;
    }

    private async Task<IReadOnlyList<PlaceDto>> CitiesAsync(SearchPlacesQuery request, CancellationToken cancellationToken)
    {
        var cities = Georgian.IsMatch(request.Search)
            ? await GeorgianCitiesAsync(request.Search.Trim(), request.Max, cancellationToken)
            : world.SearchCities(request.Search, request.Max);
        if (cities.Count == 0) return [];

        // Catalog cities keep their curated (translated) names and their destination id.
        var codes = cities.SelectMany(c => c.AirportCodes).ToList();
        var catalogAirports = await db.Airports.AsNoTracking()
            .Where(a => codes.Contains(a.IataCode) && a.DestinationId != null && a.Destination!.IsCatalog && a.Destination.IsActive)
            .Include(a => a.Destination)
            .ToListAsync(cancellationToken);
        var language = T.CurrentLanguage;

        return cities.Select(c =>
        {
            var destination = catalogAirports.FirstOrDefault(a => c.AirportCodes.Contains(a.IataCode))?.Destination;
            return destination is null
                ? new PlaceDto(c.CityCode, c.City, c.Country, c.CountryCode, c.Continent, c.AirportCodes, null)
                : new PlaceDto(c.CityCode, destination.CityIn(language), destination.CountryIn(language), c.CountryCode, destination.Region,
                    c.AirportCodes, destination.Id);
        }).ToList();
    }

    private static readonly System.Text.RegularExpressions.Regex Georgian = new("[ა-ჰ]");

    /// <summary>
    /// A world city typed in Georgian ("პარიზი", "ბარსელონა", "ბუენოს-აირესი"). The airport list has English names only and
    /// the map search finds Tbilisi streets and shops of that name, so: the catalog's Georgian city names first, then the
    /// name transliterated ("barselona") and matched with a typo or two (Barcelona).
    /// </summary>
    private async Task<IReadOnlyList<WorldCity>> GeorgianCitiesAsync(string term, int max, CancellationToken ct)
    {
        // Partial words and other cases: "პარიზ", "პარიზში", "ბარსელონაში" all start like the name without its last letter.
        var stem = term.Length > 3 && term[^1] is 'ი' or 'ა' ? term[..^1] : term;
        var catalog = await db.Destinations.AsNoTracking().Where(d => d.IsCatalog && d.IsActive).Include(d => d.Airports).ToListAsync(ct);
        var found = catalog
            .Where(d => d.Translations.Values.Any(t => t.City is { Length: > 0 } city &&
                (city.StartsWith(stem, StringComparison.OrdinalIgnoreCase) || term.StartsWith(city[..Math.Max(3, city.Length - 1)], StringComparison.OrdinalIgnoreCase))))
            // "ვენა" is Vienna before Venice (ვენეცია), which only starts the same way.
            .OrderByDescending(d => d.Translations.Values.Any(t => string.Equals(t.City, term, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(d => d.Airports.Select(a => world.FindCity(a.IataCode)).OfType<WorldCity>().Take(1))
            .ToList();

        var latin = Transliterate(term);
        // Georgian adds "-ი" to foreign names: "ლონდონი" is London, "ბუენოს-აირესი" Buenos Aires.
        var bare = latin.Length > 4 && latin.EndsWith('i') ? latin[..^1] : latin;
        foreach (var form in new[] { bare, latin }.Distinct())
        {
            var prefix = world.SearchCities(form, max).Where(c => c.City.StartsWith(form, StringComparison.OrdinalIgnoreCase)).ToList();
            found.AddRange(prefix.Count > 0 ? prefix : world.SearchCitiesLike(form, max));
        }
        return found.DistinctBy(c => c.CityCode).Take(max).ToList();
    }

    private static readonly Dictionary<char, string> Latin = new()
    {
        ['ა'] = "a", ['ბ'] = "b", ['გ'] = "g", ['დ'] = "d", ['ე'] = "e", ['ვ'] = "v", ['ზ'] = "z", ['თ'] = "t", ['ი'] = "i", ['კ'] = "k",
        ['ლ'] = "l", ['მ'] = "m", ['ნ'] = "n", ['ო'] = "o", ['პ'] = "p", ['ჟ'] = "zh", ['რ'] = "r", ['ს'] = "s", ['ტ'] = "t", ['უ'] = "u",
        ['ფ'] = "p", ['ქ'] = "k", ['ღ'] = "gh", ['ყ'] = "q", ['შ'] = "sh", ['ჩ'] = "ch", ['ც'] = "ts", ['ძ'] = "dz", ['წ'] = "ts", ['ჭ'] = "ch",
        ['ხ'] = "kh", ['ჯ'] = "j", ['ჰ'] = "h",
    };

    /// <summary>Georgian national romanization, hyphens as spaces ("ბუენოს-აირესი" → "buenos airesi").</summary>
    internal static string Transliterate(string georgian) =>
        string.Concat(georgian.Select(ch => Latin.TryGetValue(ch, out var l) ? l : ch == '-' ? " " : char.ToLowerInvariant(ch).ToString())).Trim();
}

public sealed class GetNearestAirportHandler(IApplicationDbContext db, IVisitorLocator locator) : IRequestHandler<GetNearestAirportQuery, AirportDto?>
{
    public async Task<AirportDto?> Handle(GetNearestAirportQuery request, CancellationToken cancellationToken)
    {
        var location = await locator.LocateAsync(request.IpAddress, cancellationToken);
        if (location is null) return null;

        var departures = db.Airports.AsNoTracking().Where(a => a.IsDepartureOption);
        Airport? airport = null;
        if (location.Iata is { } iata)
        {
            // The locator returns a city (or airport) code: prefer a curated airport of that city, then the one closest to the visitor.
            var cityAirports = await departures.Where(a => a.IataCode == iata || a.CityCode == iata).ToListAsync(cancellationToken);
            airport = cityAirports
                .OrderByDescending(a => a.DestinationId != null || a.Translations.Count > 0)
                .ThenBy(a => Geo.DistanceKm(a.Latitude, a.Longitude, location.Latitude, location.Longitude))
                .FirstOrDefault();
        }
        if (airport is null)
        {
            var all = await departures.ToListAsync(cancellationToken);
            airport = all.MinBy(a => Geo.DistanceKm(a.Latitude, a.Longitude, location.Latitude, location.Longitude));
        }
        return airport?.ToDto();
    }
}

internal static class Geo
{
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2) +
                Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return 2 * 6371 * Math.Asin(Math.Sqrt(a));
    }
}
