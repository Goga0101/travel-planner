using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TravelPlanner.Application.Common;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Infrastructure.Persistence;

namespace TravelPlanner.Infrastructure.Catalog;

/// <summary>Idempotently upserts the catalog file into the database (by destination code and airport IATA code).</summary>
public sealed class CatalogSeeder(AppDbContext db, CatalogFileReader reader, IOutboundLinkPolicy linkPolicy, ILogger<CatalogSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var catalog = reader.Document;
        var destinations = await db.Destinations.Include(d => d.TransportLinks).ToListAsync(cancellationToken);
        var airports = await db.Airports.ToListAsync(cancellationToken);
        var codesInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in catalog.Destinations)
        {
            codesInFile.Add(source.Code);
            var destination = destinations.FirstOrDefault(d => d.Code == source.Code);
            if (destination is null)
            {
                destination = new Destination(source.Code, source.City, source.CountryCode.ToUpperInvariant(), source.Country, source.Latitude, source.Longitude);
                db.Destinations.Add(destination);
                destinations.Add(destination);
            }

            destination.City = source.City;
            destination.Country = source.Country;
            destination.CountryCode = source.CountryCode.ToUpperInvariant();
            destination.Region = source.Region;
            destination.Latitude = source.Latitude;
            destination.Longitude = source.Longitude;
            destination.Popularity = source.Popularity;
            destination.Description = source.Description;
            destination.IsActive = source.IsActive;
            destination.BookingComCityId = source.BookingComCityId;
            destination.TaxiBaseFare = source.TaxiTariff?.BaseFare;
            destination.TaxiPerKm = source.TaxiTariff?.PerKm;
            destination.TaxiTariffCurrency = source.TaxiTariff?.Currency?.ToUpperInvariant();
            destination.TaxiTariffSource = source.TaxiTariff?.Source;
            destination.Translations = source.Translations.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value);

            // Transport links are curated data owned by the catalog file: replace them.
            db.DestinationTransportLinks.RemoveRange(destination.TransportLinks);
            destination.TransportLinks.Clear();
            foreach (var link in source.TransportLinks.DistinctBy(l => l.Url))
            {
                if (linkPolicy.Sanitize(link.Url) is null)
                    logger.LogWarning("Catalog link {Url} for {Destination} is not on the outbound allow-list and will be hidden from users", link.Url, source.Code);
                destination.TransportLinks.Add(new DestinationTransportLink(link.Kind, link.Name, link.Url)
                {
                    DestinationId = destination.Id,
                    Phone = link.Phone,
                    Notes = link.Notes,
                    IsVerified = link.IsVerified,
                    VerifiedOn = link.VerifiedOn,
                    NameTranslations = link.NameTranslations.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value)
                });
            }

            foreach (var a in source.Airports)
                Upsert(airports, a, source.City, source.CountryCode, destination.Id,
                    source.Translations.ToDictionary(kv => kv.Key, kv => kv.Value.City ?? source.City));
        }

        foreach (var a in catalog.DepartureAirports)
            Upsert(airports, a, a.City ?? a.Name, a.CountryCode ?? "", null, a.CityTranslations);

        foreach (var stale in destinations.Where(d => !codesInFile.Contains(d.Code) && d.IsActive))
        {
            stale.IsActive = false;
            logger.LogInformation("Destination {Code} is no longer in the catalog and was deactivated", stale.Code);
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Catalog seeded: {Destinations} destinations, {Airports} airports", catalog.Destinations.Count, catalog.AllAirports.Count());

        void Upsert(List<Airport> existing, CatalogAirport source, string city, string countryCode, Guid? destinationId,
            IReadOnlyDictionary<string, string> cityTranslations)
        {
            var iata = source.Iata.ToUpperInvariant();
            var airport = existing.FirstOrDefault(x => x.IataCode == iata);
            if (airport is null)
            {
                airport = new Airport(iata, source.Name, city, countryCode.ToUpperInvariant(), source.Latitude, source.Longitude);
                db.Airports.Add(airport);
                existing.Add(airport);
            }
            airport.Name = source.Name;
            airport.City = city;
            airport.CountryCode = countryCode.ToUpperInvariant();
            airport.Latitude = source.Latitude;
            airport.Longitude = source.Longitude;
            airport.IsDepartureOption = source.IsDepartureOption;
            airport.OfficialWebsiteUrl = source.OfficialWebsiteUrl;
            airport.DestinationId = destinationId;
            airport.Translations = source.NameTranslations.Keys.Union(cityTranslations.Keys)
                .ToDictionary(lang => lang.ToLowerInvariant(), lang => new AirportTranslation
                {
                    Name = source.NameTranslations.GetValueOrDefault(lang),
                    City = cityTranslations.GetValueOrDefault(lang)
                });
        }
    }
}
