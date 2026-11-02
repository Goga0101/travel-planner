using MediatR;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.TripPlans;

/// <summary>MaxNightly: only hotels whose cheapest nightly rate fits (searched among many more of the city's hotels).</summary>
public sealed record GetHotelPricesQuery(Guid Id, decimal? MaxNightly = null) : IRequest<HotelPricesDto?>;

/// <summary>Url books with that agency: Booking.com directly (hotel and dates pre-filled), the others through the hotel's TripAdvisor page.</summary>
public sealed record AgencyRateDto(string Code, string Name, decimal Nightly, string? Url, bool ViaTripAdvisor);

public sealed record HotelPriceDto(
    string Name,
    double? Rating,
    int? ReviewCount,
    int? DistanceFromCentreMeters,
    decimal? BookingComNightly,
    decimal LowestNightly,
    string LowestAgency,
    decimal StayTotal,
    IReadOnlyList<AgencyRateDto> Rates,
    string? BookingUrl,
    string? MapUrl,
    string? TransitDirectionsUrl,
    string? CompareUrl,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>
/// Indicative prices per night for the plan's dates, guests and rooms. They are not added to the plan total;
/// EstimatedTripTotal only shows what the trip could cost with a typical stay.
/// </summary>
public sealed record HotelPricesDto(
    string Source,
    string Currency,
    DateTimeOffset RetrievedAtUtc,
    int Nights,
    int Rooms,
    int Adults,
    decimal? BookingComNightlyMin,
    decimal? BookingComNightlyMax,
    decimal LowestNightly,
    decimal TypicalNightly,
    decimal EstimatedStayFrom,
    decimal EstimatedStayTypical,
    decimal EstimatedTripTotal,
    IReadOnlyList<HotelPriceDto> Hotels);

public sealed class GetHotelPricesHandler(
    IApplicationDbContext db,
    IHotelPriceSource prices,
    IAccommodationSearchLinkBuilder bookingLinks,
    IOutboundLinkPolicy linkPolicy)
    : IRequestHandler<GetHotelPricesQuery, HotelPricesDto?>
{
    public async Task<HotelPricesDto?> Handle(GetHotelPricesQuery request, CancellationToken cancellationToken)
    {
        var plan = await TripPlanLoader.LoadAsync(db, request.Id, tracking: false, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.Id);
        var destination = plan.Destination!;
        var rooms = Math.Max(1, (plan.Travelers + 1) / 2);
        var nights = plan.Nights;
        if (nights <= 0) return null;

        var result = await prices.GetAsync(
            new HotelPriceQuery(destination.Code, plan.DepartureDate, plan.ReturnDate, plan.Travelers, rooms, plan.Currency,
                destination.Latitude, destination.Longitude, request.MaxNightly is > 0 ? request.MaxNightly : null), cancellationToken);
        if (result is null || result.Hotels.Count == 0) return null;
        if (request.MaxNightly is > 0 and var max)
        {
            result = result with { Hotels = result.Hotels.Where(h => h.Rates.Min(r => r.Nightly) <= max).ToList() };
            if (result.Hotels.Count == 0) return null;
        }

        var airport = GetNearbyPlacesHandler.ArrivalAirport(plan);
        var hotels = result.Hotels
            .Select(h =>
            {
                var lowest = h.Rates.MinBy(r => r.Nightly)!;
                var booking = h.Rates.FirstOrDefault(r => r.Code == "BookingCom");
                var hasGeo = h.Latitude is not null && h.Longitude is not null;
                var pageUrl = NearbyLinks.TripAdvisorWithStay(h.PageUrl, plan.DepartureDate, plan.ReturnDate, plan.Travelers, rooms);
                // Only hotels with a Booking.com rate are sold there; a name search for any other hotel finds nothing.
                var bookingUrl = booking is null ? null
                    : linkPolicy.Sanitize(bookingLinks.Build(h.Name, destination.City, plan.DepartureDate, plan.ReturnDate, plan.Travelers, rooms)?.Url);
                return new HotelPriceDto(
                    h.Name,
                    h.Rating,
                    h.ReviewCount,
                    hasGeo ? NearbyLinks.DistanceMeters(destination.Latitude, destination.Longitude, h.Latitude!.Value, h.Longitude!.Value) : null,
                    booking?.Nightly,
                    lowest.Nightly,
                    lowest.Name,
                    lowest.Nightly * nights,
                    h.Rates.Select(r => r.Code == "BookingCom" && bookingUrl is not null
                        ? new AgencyRateDto(r.Code, r.Name, r.Nightly, bookingUrl, false)
                        : new AgencyRateDto(r.Code, r.Name, r.Nightly, pageUrl, pageUrl is not null)).ToList(),
                    bookingUrl,
                    linkPolicy.Sanitize(NearbyLinks.MapSearch($"{h.Name}, {destination.City}")),
                    hasGeo && airport is not null
                        ? linkPolicy.Sanitize(NearbyLinks.TransitDirections(airport.Latitude, airport.Longitude, h.Latitude!.Value, h.Longitude!.Value))
                        : null,
                    pageUrl,
                    h.Latitude,
                    h.Longitude);
            })
            .OrderBy(h => h.LowestNightly)
            .ToList();

        var bookingRates = hotels.Where(h => h.BookingComNightly is not null).Select(h => h.BookingComNightly!.Value).ToList();
        var lowestNightly = hotels.Min(h => h.LowestNightly);
        var typicalNightly = Median(hotels.Select(h => h.LowestNightly));

        return new HotelPricesDto(
            result.Source,
            result.Currency,
            result.RetrievedAtUtc,
            nights,
            rooms,
            plan.Travelers,
            bookingRates.Count > 0 ? bookingRates.Min() : null,
            bookingRates.Count > 0 ? bookingRates.Max() : null,
            lowestNightly,
            typicalNightly,
            lowestNightly * nights,
            typicalNightly * nights,
            plan.TotalEstimatedCost + typicalNightly * nights,
            hotels);
    }

    public static decimal Median(IEnumerable<decimal> values)
    {
        var sorted = values.Order().ToList();
        if (sorted.Count == 0) return 0;
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : Math.Round((sorted[mid - 1] + sorted[mid]) / 2, 2);
    }
}
