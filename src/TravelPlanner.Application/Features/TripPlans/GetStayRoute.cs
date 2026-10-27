using System.Globalization;
using System.Text.Json;
using FluentValidation;
using MediatR;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Features.TripPlans;

/// <summary>
/// Ways from the arrival airport to the place the visitor will stay (the city centre when none is chosen) and back,
/// with the city's ticket guide.
/// </summary>
public sealed record GetStayRouteQuery(Guid PlanId, double? Latitude, double? Longitude, string? Name) : IRequest<StayRouteDto>;

public sealed record TransitLegDto(
    string Mode, string? Line, string? Headsign, string From, string To, string DepartureTime, string ArrivalTime, int DurationMinutes,
    string? Agency, int? Stops, string? Color);

public sealed record TransitItineraryDto(int DurationMinutes, int Transfers, string DepartureTime, string ArrivalTime, IReadOnlyList<TransitLegDto> Legs);

public sealed record RouteDirectionDto(
    string FromName, string ToName, DateOnly TimetableDate, bool OnTravelDate, string WishedTime,
    IReadOnlyList<TransitItineraryDto> Itineraries, string? GoogleTransitUrl, string? GoogleDriveUrl, string? UberUrl);

public sealed record GuideOptionDto(
    string Mode, string Name, string? From, string? To, string? Ticket, decimal? Price, string? Currency, string? WhereToBuy, string? Payment,
    string? Duration, string? Frequency, string? Url);

public sealed record GuideFareDto(string? Name, decimal? Price, string? Currency);

public sealed record TransitGuideDto(
    string Airport, IReadOnlyList<GuideOptionDto> Options, GuideFareDto? CityTicket, GuideFareDto? DayPass, bool? Contactless,
    IReadOnlyList<TransitGuideApp> Apps, IReadOnlyList<string> Tips, IReadOnlyList<string> Sources, string? CheckedOn);

/// <summary>ToStay/ToAirport are null when the airport is unknown; RouterName is null when no journey planner is available.</summary>
public sealed record StayRouteDto(
    string? AirportCode, string? AirportName, string StayName, bool StayIsCentre,
    RouteDirectionDto? ToStay, RouteDirectionDto? ToAirport, TransitGuideDto? Guide, string? RouterName);

public sealed class GetStayRouteValidator : AbstractValidator<GetStayRouteQuery>
{
    public GetStayRouteValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude.HasValue);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude.HasValue);
        RuleFor(x => x).Must(x => x.Latitude.HasValue == x.Longitude.HasValue).WithMessage("Latitude and longitude go together.");
        RuleFor(x => x.Name).MaximumLength(200);
    }
}

public sealed class GetStayRouteHandler(
    IApplicationDbContext db,
    ITransitRouter router,
    ITransitGuideSource guides,
    IWorldPlaces world,
    IOutboundLinkPolicy linkPolicy) : IRequestHandler<GetStayRouteQuery, StayRouteDto>
{
    /// <summary>Time to leave the airport after landing (passport control, bags), and to reach it before take-off.</summary>
    private static readonly TimeSpan AfterLanding = TimeSpan.FromMinutes(50);
    private static readonly TimeSpan BeforeTakeOff = TimeSpan.FromHours(3);

    public async Task<StayRouteDto> Handle(GetStayRouteQuery request, CancellationToken cancellationToken)
    {
        var plan = await TripPlanLoader.LoadAsync(db, request.PlanId, tracking: false, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.PlanId);
        var destination = plan.Destination!;
        var language = T.CurrentLanguage;
        var airport = GetNearbyPlacesHandler.ArrivalAirport(plan);
        var stayIsCentre = request.Latitude is null;
        var (stayLat, stayLon) = stayIsCentre ? (destination.Latitude, destination.Longitude) : (request.Latitude!.Value, request.Longitude!.Value);
        var stayName = stayIsCentre ? T.Get("route.cityCentre", destination.CityIn(language)) : request.Name?.Trim() is { Length: > 0 } n ? n : T.Get("route.yourStay");

        FlightItemDetails? flight = null;
        if (plan.Items.FirstOrDefault(i => i.Type == PlanItemType.Flight) is { } item)
        {
            try { flight = JsonSerializer.Deserialize<FlightItemDetails>(item.DetailsJson, AppJson.Options); }
            catch (JsonException) { }
        }

        var zone = TimeZoneFor(airport?.IataCode ?? destination.Code);
        RouteDirectionDto? toStay = null, toAirport = null;
        if (airport is not null)
        {
            var airportName = airport.NameIn(language);
            var arrive = (flight?.Outbound.ArrivalLocal ?? plan.DepartureDate.ToDateTime(new TimeOnly(12, 0))).Add(AfterLanding);
            var leave = (flight?.Inbound.DepartureLocal ?? plan.ReturnDate.ToDateTime(new TimeOnly(15, 0))).Subtract(BeforeTakeOff);
            var tasks = new[]
            {
                DirectionAsync(airportName, airport.Latitude, airport.Longitude, stayName, stayLat, stayLon, arrive, zone, cancellationToken),
                DirectionAsync(stayName, stayLat, stayLon, airportName, airport.Latitude, airport.Longitude, leave, zone, cancellationToken)
            };
            var results = await Task.WhenAll(tasks);
            (toStay, toAirport) = (results[0], results[1]);
        }

        return new StayRouteDto(airport?.IataCode, airport?.NameIn(language), stayName, stayIsCentre, toStay, toAirport,
            Guide(guides.Get(destination.Code), language), router.IsConfigured ? router.Name : null);
    }

    private async Task<RouteDirectionDto> DirectionAsync(string fromName, double fromLat, double fromLon, string toName, double toLat, double toLon,
        DateTime wished, TimeZoneInfo? zone, CancellationToken ct)
    {
        var offset = zone?.GetUtcOffset(wished) ?? TimeSpan.Zero;
        var itineraries = zone is null || !router.IsConfigured
            ? []
            : await router.PlanAsync(new TransitRouteQuery(fromLat, fromLon, toLat, toLon, wished, offset), ct);
        var timetableDate = itineraries.Count > 0 ? DateOnly.FromDateTime(itineraries[0].DepartureLocal) : DateOnly.FromDateTime(wished);
        return new RouteDirectionDto(fromName, toName, timetableDate, timetableDate == DateOnly.FromDateTime(wished),
            wished.ToString("HH:mm", CultureInfo.InvariantCulture),
            itineraries.Select(ToDto).ToList(),
            linkPolicy.Sanitize(NearbyLinks.TransitDirections(fromLat, fromLon, toLat, toLon)),
            linkPolicy.Sanitize(NearbyLinks.TransitDirections(fromLat, fromLon, toLat, toLon).Replace("travelmode=transit", "travelmode=driving")),
            linkPolicy.Sanitize(NearbyLinks.Uber(fromLat, fromLon, fromName, toLat, toLon, toName)));
    }

    private static TransitItineraryDto ToDto(TransitItinerary i) =>
        new(i.DurationMinutes, i.Transfers, Hm(i.DepartureLocal), Hm(i.ArrivalLocal),
            i.Legs.Select(l => new TransitLegDto(l.Mode, l.Line, l.Headsign, l.From, l.To, Hm(l.DepartureLocal), Hm(l.ArrivalLocal),
                l.DurationMinutes, l.Agency, l.Stops, l.Color)).ToList());

    private static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    private TimeZoneInfo? TimeZoneFor(string code)
    {
        var id = world.FindCity(code)?.TimeZone;
        if (id is null) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }

    private static TransitGuideDto? Guide(TransitGuide? g, string lang) =>
        g is null
            ? null
            : new TransitGuideDto(g.Airport,
                g.Options.Select(o => new GuideOptionDto(o.Mode, o.Name, o.From, o.To, o.Ticket?.In(lang), o.Price, o.Currency, o.WhereToBuy?.In(lang),
                    o.Payment?.In(lang), o.Duration?.In(lang), o.Frequency?.In(lang), NearbyLinks.CuratedHttps(o.Url))).ToList(),
                Fare(g.CityTicket, lang), Fare(g.DayPass, lang), g.Contactless,
                g.Apps.Where(a => !string.IsNullOrWhiteSpace(a.Name)).Select(a => a with { Url = NearbyLinks.CuratedHttps(a.Url) }).ToList(),
                g.Tips.Select(t => t.In(lang)).OfType<string>().ToList(),
                g.Sources.Select(NearbyLinks.CuratedHttps).OfType<string>().ToList(),
                g.CheckedOn);

    private static GuideFareDto? Fare(TransitGuideFare? f, string lang) =>
        f is null || (f.Price is null && f.Name?.In(lang) is null) ? null : new GuideFareDto(f.Name?.In(lang), f.Price, f.Currency);
}
