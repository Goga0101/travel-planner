using System.Globalization;
using System.Text.Json;
using MediatR;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Dtos;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Features.TripPlans;

public sealed record GetNearbyPlacesQuery(Guid Id) : IRequest<NearbyPlacesDto>;

public sealed record NearbyAirportDto(string IataCode, string Name, double Latitude, double Longitude, string? OfficialWebsiteUrl);

/// <summary>A hotel from OpenStreetMap. There is no price: BookingUrl opens a Booking.com search for it with the plan's dates.</summary>
public sealed record NearbyHotelDto(
    string Id,
    string Name,
    string Kind,
    int? Stars,
    double Latitude,
    double Longitude,
    int DistanceFromAirportMeters,
    int DistanceFromCentreMeters,
    string? Address,
    string? Phone,
    string? Website,
    string? BookingUrl,
    string? MapUrl,
    string? TransitDirectionsUrl,
    string? UberUrl);

public sealed record TransitLineDto(string Mode, string? Ref, string? Name, string? From, string? To, string? Operator, string? Network, string? Website);

public sealed record TaxiRankDto(string? Name, string? Operator, string? Phone, int DistanceFromAirportMeters, string? MapUrl);

public sealed record TaxiCompanyDto(string Name, string? Phone, string? Website);

public sealed record NearbyPlacesDto(
    string Attribution,
    DateOnly? RetrievedOn,
    NearbyAirportDto? Airport,
    IReadOnlyList<NearbyHotelDto> AirportHotels,
    IReadOnlyList<NearbyHotelDto> CentreHotels,
    IReadOnlyList<TransitLineDto> TransitLines,
    IReadOnlyList<TaxiRankDto> TaxiRanks,
    IReadOnlyList<TaxiCompanyDto> TaxiCompanies,
    string? CentreTransitDirectionsUrl,
    string? CentreUberUrl,
    string? HotelSearchUrl);

/// <summary>
/// Hotels near the arrival airport and the city centre, the public-transport lines serving that airport and local taxis,
/// each with outbound links pre-filled from the plan (dates, travellers, airport → hotel route).
/// </summary>
public sealed class GetNearbyPlacesHandler(
    IApplicationDbContext db,
    INearbyPlacesSource source,
    IAccommodationSearchLinkBuilder bookingLinks,
    IOutboundLinkPolicy linkPolicy)
    : IRequestHandler<GetNearbyPlacesQuery, NearbyPlacesDto>
{
    public async Task<NearbyPlacesDto> Handle(GetNearbyPlacesQuery request, CancellationToken cancellationToken)
    {
        var plan = await TripPlanLoader.LoadAsync(db, request.Id, tracking: false, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.Id);
        var destination = plan.Destination!;
        var airport = ArrivalAirport(plan);
        var snapshot = source.Find(destination.Code);
        var nearAirport = snapshot?.Airports.FirstOrDefault(a => string.Equals(a.Iata, airport?.IataCode, StringComparison.OrdinalIgnoreCase));
        var rooms = Math.Max(1, (plan.Travelers + 1) / 2);
        var lang = T.CurrentLanguage;

        NearbyHotelDto Hotel(NearbyHotel h)
        {
            var name = lang == T.English && !string.IsNullOrWhiteSpace(h.NameEn) ? h.NameEn! : h.Name;
            var booking = bookingLinks.Build(h.Name, destination.City, plan.DepartureDate, plan.ReturnDate, plan.Travelers, rooms);
            return new NearbyHotelDto(
                h.OsmId, name, h.Kind, h.Stars, h.Latitude, h.Longitude,
                airport is null ? 0 : NearbyLinks.DistanceMeters(airport.Latitude, airport.Longitude, h.Latitude, h.Longitude),
                NearbyLinks.DistanceMeters(destination.Latitude, destination.Longitude, h.Latitude, h.Longitude),
                h.Address, h.Phone, NearbyLinks.CuratedHttps(h.Website),
                linkPolicy.Sanitize(booking?.Url),
                linkPolicy.Sanitize(NearbyLinks.MapSearch($"{h.Name}, {h.Address ?? destination.City}")),
                airport is null ? null : linkPolicy.Sanitize(NearbyLinks.TransitDirections(airport.Latitude, airport.Longitude, h.Latitude, h.Longitude)),
                airport is null ? null : linkPolicy.Sanitize(NearbyLinks.Uber(airport.Latitude, airport.Longitude, airport.IataCode, h.Latitude, h.Longitude, h.Name)));
        }

        var centreName = destination.CityIn(lang);
        return new NearbyPlacesDto(
            source.Attribution,
            snapshot?.RetrievedOn,
            airport is null ? null : new NearbyAirportDto(airport.IataCode, airport.NameIn(lang), airport.Latitude, airport.Longitude, linkPolicy.Sanitize(airport.OfficialWebsiteUrl)),
            nearAirport?.Hotels.Select(Hotel).OrderBy(h => h.DistanceFromAirportMeters).ToList() ?? [],
            snapshot?.Centre.Hotels.Select(Hotel).OrderBy(h => h.DistanceFromCentreMeters).ToList() ?? [],
            nearAirport?.TransitLines.Select(l => new TransitLineDto(l.Mode, l.Ref, l.Name, l.From, l.To, l.Operator, l.Network, NearbyLinks.CuratedHttps(l.Website))).ToList() ?? [],
            nearAirport?.TaxiRanks.Select(r => new TaxiRankDto(r.Name, r.Operator, r.Phone,
                airport is null ? 0 : NearbyLinks.DistanceMeters(airport.Latitude, airport.Longitude, r.Latitude, r.Longitude),
                linkPolicy.Sanitize(NearbyLinks.MapPoint(r.Latitude, r.Longitude)))).ToList() ?? [],
            snapshot?.TaxiCompanies.Select(c => new TaxiCompanyDto(c.Name, c.Phone, NearbyLinks.CuratedHttps(c.Website))).ToList() ?? [],
            airport is null ? null : linkPolicy.Sanitize(NearbyLinks.TransitDirections(airport.Latitude, airport.Longitude, destination.Latitude, destination.Longitude)),
            airport is null ? null : linkPolicy.Sanitize(NearbyLinks.Uber(airport.Latitude, airport.Longitude, airport.IataCode, destination.Latitude, destination.Longitude, centreName)),
            linkPolicy.Sanitize(bookingLinks.Build(destination.City, destination.Country, plan.DepartureDate, plan.ReturnDate, plan.Travelers, rooms)?.Url));
    }

    /// <summary>The airport the outbound flight lands at, falling back to the destination's first airport.</summary>
    private static Airport? ArrivalAirport(TripPlan plan)
    {
        var airports = plan.Destination!.Airports;
        var flight = plan.Items.FirstOrDefault(i => i.Type == PlanItemType.Flight);
        if (flight is not null)
        {
            try
            {
                var details = JsonSerializer.Deserialize<FlightItemDetails>(flight.DetailsJson, AppJson.Options);
                var iata = details?.Outbound.ArrivalAirport;
                var match = airports.FirstOrDefault(a => string.Equals(a.IataCode, iata, StringComparison.OrdinalIgnoreCase));
                if (match is not null) return match;
            }
            catch (JsonException)
            {
                // Fall through to the default airport.
            }
        }
        return airports.OrderBy(a => a.IataCode).FirstOrDefault();
    }
}

/// <summary>Builders for the map / directions / ride-hailing deep links shown next to nearby places. Pure functions.</summary>
public static class NearbyLinks
{
    public static int DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        var a = Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2)
                + Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return (int)Math.Round(2 * 6_371_000 * Math.Asin(Math.Sqrt(a)));
    }

    public static string MapSearch(string query) =>
        "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(query);

    public static string MapPoint(double lat, double lon) =>
        "https://www.google.com/maps/search/?api=1&query=" + Coord(lat) + "," + Coord(lon);

    /// <summary>Google Maps public-transport directions between two points (Google computes the actual lines and times).</summary>
    public static string TransitDirections(double fromLat, double fromLon, double toLat, double toLon) =>
        $"https://www.google.com/maps/dir/?api=1&origin={Coord(fromLat)},{Coord(fromLon)}&destination={Coord(toLat)},{Coord(toLon)}&travelmode=transit";

    /// <summary>Uber universal link with pickup and drop-off pre-filled; Uber shows the fare estimate and availability.</summary>
    public static string Uber(double fromLat, double fromLon, string fromName, double toLat, double toLon, string toName) =>
        "https://m.uber.com/ul/?action=setPickup"
        + $"&pickup[latitude]={Coord(fromLat)}&pickup[longitude]={Coord(fromLon)}&pickup[nickname]={Uri.EscapeDataString(fromName)}"
        + $"&dropoff[latitude]={Coord(toLat)}&dropoff[longitude]={Coord(toLon)}&dropoff[nickname]={Uri.EscapeDataString(toName)}";

    /// <summary>
    /// Websites from the reviewed OpenStreetMap snapshot (catalog/nearby.json) are shown as-is when they are plain HTTPS URLs.
    /// They are not on the booking allow-list because every hotel has its own domain; the server never fetches them.
    /// </summary>
    public static string? CuratedHttps(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return null;
        return uri.AbsoluteUri;
    }

    private static string Coord(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
