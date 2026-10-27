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

public sealed class SearchPlacesHandler(IApplicationDbContext db, IWorldPlaces world) : IRequestHandler<SearchPlacesQuery, IReadOnlyList<PlaceDto>>
{
    public async Task<IReadOnlyList<PlaceDto>> Handle(SearchPlacesQuery request, CancellationToken cancellationToken)
    {
        var cities = world.SearchCities(request.Search, request.Max);
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
