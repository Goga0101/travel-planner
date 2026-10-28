using System.Globalization;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.Transit;

// ---------- Place search ----------

/// <summary>Places anywhere in the world by name (addresses, streets, stops, landmarks); Near biases the results.</summary>
public sealed record SearchPlacesQuery(string Text, double? NearLatitude, double? NearLongitude) : IRequest<IReadOnlyList<PlaceSuggestionDto>>;

/// <summary>Kind is address, stop, city or place.</summary>
public sealed record PlaceSuggestionDto(string Name, string? Area, string? CountryCode, double Latitude, double Longitude, string Kind);

/// <summary>The address at a point (the visitor's own location).</summary>
public sealed record ReversePlaceQuery(double Latitude, double Longitude) : IRequest<PlaceSuggestionDto?>;

// ---------- Journey ----------

/// <summary>Public-transport journeys between any two points, leaving now or at the given local time at the origin.</summary>
public sealed record PlanJourneyQuery(
    double FromLatitude, double FromLongitude, string? FromName, double ToLatitude, double ToLongitude, string? ToName, DateTime? LocalTime)
    : IRequest<JourneyDto>;

/// <summary>
/// TimeZone is the origin's IANA zone (times are local there); CountryCode drives the taxi section. Coaches lists the city's
/// intercity minibus stations for trips to another region where timetables are not published (Tbilisi for now).
/// </summary>
public sealed record JourneyDto(
    string FromName, string ToName, string? TimeZone, string? CountryCode, int DistanceMeters, RouteDirectionDto Direction, string? RouterName,
    IReadOnlyList<CoachStationDto> Coaches);

// ---------- Live vehicles ----------

public sealed record LiveVehiclesQuery(double MinLatitude, double MinLongitude, double MaxLatitude, double MaxLongitude) : IRequest<LiveVehiclesDto>;

/// <summary>One stretch between two stops: Unix seconds (UTC) for departure/arrival, the next stop and the shape (encoded polyline, precision 5).</summary>
public sealed record VehicleStretchDto(long Departure, long Arrival, string To, string Shape);

/// <summary>A vehicle on its run; RealTime when the operator publishes live positions/delays, otherwise it follows the timetable.</summary>
public sealed record VehicleDto(string Id, string Mode, string? Line, string? Color, bool RealTime, IReadOnlyList<VehicleStretchDto> Stretches);

/// <summary>ServerTime in Unix seconds; TooLarge when the area is too big to show individual vehicles (zoom in).</summary>
public sealed record LiveVehiclesDto(long ServerTime, bool TooLarge, IReadOnlyList<VehicleDto> Vehicles, string? Source);

// ---------- Taxis ----------

public sealed record LocalTaxisQuery(double Latitude, double Longitude, string? CountryCode, double? ToLatitude, double? ToLongitude) : IRequest<TaxiOptionsDto>;

/// <summary>A ride-hailing app known in the country; RideUrl opens it with the trip pre-filled where the app supports it.</summary>
public sealed record TaxiAppDto(string Name, string Url, string? RideUrl);

public sealed record LocalTaxiDto(string? Name, string? Operator, string? Phone, string? Website, int DistanceMeters, string MapUrl, bool IsCompany);

/// <summary>SearchUrl looks up taxi companies (with phone numbers) around the point on Google Maps.</summary>
public sealed record TaxiOptionsDto(string? CountryCode, IReadOnlyList<TaxiAppDto> Apps, IReadOnlyList<LocalTaxiDto> Local, string SearchUrl, string Source);

public sealed class SearchPlacesValidator : AbstractValidator<SearchPlacesQuery>
{
    public SearchPlacesValidator()
    {
        RuleFor(x => x.Text).NotEmpty().MinimumLength(2).MaximumLength(120);
        RuleFor(x => x.NearLatitude).InclusiveBetween(-90, 90).When(x => x.NearLatitude.HasValue);
        RuleFor(x => x.NearLongitude).InclusiveBetween(-180, 180).When(x => x.NearLongitude.HasValue);
    }
}

public sealed class ReversePlaceValidator : AbstractValidator<ReversePlaceQuery>
{
    public ReversePlaceValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
    }
}

public sealed class PlanJourneyValidator : AbstractValidator<PlanJourneyQuery>
{
    public PlanJourneyValidator()
    {
        RuleFor(x => x.FromLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.ToLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.FromLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.ToLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.FromName).MaximumLength(200);
        RuleFor(x => x.ToName).MaximumLength(200);
    }
}

public sealed class LiveVehiclesValidator : AbstractValidator<LiveVehiclesQuery>
{
    public LiveVehiclesValidator()
    {
        RuleFor(x => x.MinLatitude).InclusiveBetween(-90, 90).LessThan(x => x.MaxLatitude);
        RuleFor(x => x.MaxLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.MinLongitude).InclusiveBetween(-180, 180).LessThan(x => x.MaxLongitude);
        RuleFor(x => x.MaxLongitude).InclusiveBetween(-180, 180);
    }
}

public sealed class LocalTaxisValidator : AbstractValidator<LocalTaxisQuery>
{
    public LocalTaxisValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.CountryCode).Length(2).When(x => x.CountryCode is not null);
        RuleFor(x => x.ToLatitude).InclusiveBetween(-90, 90).When(x => x.ToLatitude.HasValue);
        RuleFor(x => x.ToLongitude).InclusiveBetween(-180, 180).When(x => x.ToLongitude.HasValue);
    }
}

public sealed class LiveTransitHandler(
    ILiveTransit live,
    ITransitRouter router,
    ILocalTaxiSource taxis,
    ITransitGuideSource guides,
    IApplicationDbContext db,
    IOutboundLinkPolicy linkPolicy,
    ITransitStopSource stops,
    TimeProvider timeProvider)
    : IRequestHandler<SearchPlacesQuery, IReadOnlyList<PlaceSuggestionDto>>,
      IRequestHandler<ReversePlaceQuery, PlaceSuggestionDto?>,
      IRequestHandler<PlanJourneyQuery, JourneyDto>,
      IRequestHandler<LiveVehiclesQuery, LiveVehiclesDto>,
      IRequestHandler<LocalTaxisQuery, TaxiOptionsDto>
{
    /// <summary>Larger areas hold thousands of vehicles; the map asks the visitor to zoom in instead.</summary>
    public const double MaxSpanDegrees = 0.35;
    /// <summary>A catalog city's ticket guide applies to journeys starting this close to its centre.</summary>
    private const int GuideRadiusMeters = 40_000;

    public async Task<IReadOnlyList<PlaceSuggestionDto>> Handle(SearchPlacesQuery request, CancellationToken cancellationToken)
    {
        var places = await live.GeocodeAsync(request.Text, T.CurrentLanguage, request.NearLatitude, request.NearLongitude, cancellationToken);
        return places.Select(ToDto).ToList();
    }

    public async Task<PlaceSuggestionDto?> Handle(ReversePlaceQuery request, CancellationToken cancellationToken) =>
        await live.ReverseAsync(request.Latitude, request.Longitude, cancellationToken) is { } p ? ToDto(p) : null;

    public async Task<JourneyDto> Handle(PlanJourneyQuery request, CancellationToken cancellationToken)
    {
        var origin = await live.ReverseAsync(request.FromLatitude, request.FromLongitude, cancellationToken);
        var zone = Zone(origin?.TimeZone);
        var nowUtc = timeProvider.GetUtcNow();
        var offset = zone?.GetUtcOffset(nowUtc) ?? TimeSpan.Zero;
        // "Now" plus a minute: the router treats only future departures as today's.
        var wished = request.LocalTime ?? nowUtc.ToOffset(offset).DateTime.AddMinutes(1);
        if (zone is not null) offset = zone.GetUtcOffset(wished);

        var fromName = Label(request.FromName ?? origin?.Name);
        var toName = Label(request.ToName);
        var itineraries = router.IsConfigured
            ? await router.PlanAsync(new TransitRouteQuery(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude,
                wished, offset, AnyDistance: true), cancellationToken)
            : [];
        var timetableDate = itineraries.Count > 0 ? DateOnly.FromDateTime(itineraries[0].DepartureLocal) : DateOnly.FromDateTime(wished);
        // No timetable here: fall back to the lines that call near both ends (OpenStreetMap).
        var estimated = itineraries.Count > 0
            ? []
            : await StopLineRoutes.FindAsync(stops, request.FromLatitude, request.FromLongitude, fromName, request.ToLatitude, request.ToLongitude,
                toName, wished, cancellationToken);
        var direction = new RouteDirectionDto(fromName, toName, timetableDate, timetableDate == DateOnly.FromDateTime(wished),
            wished.ToString("HH:mm", CultureInfo.InvariantCulture),
            itineraries.Select(i => new TransitItineraryDto(i.DurationMinutes, i.Transfers, Hm(i.DepartureLocal), Hm(i.ArrivalLocal),
                i.Legs.Select(l => new TransitLegDto(l.Mode, l.Line, l.Headsign, l.From, l.To, Hm(l.DepartureLocal), Hm(l.ArrivalLocal),
                    l.DurationMinutes, l.Agency, l.Stops, l.Color, Path: l.Path)).ToList())).Concat(estimated).ToList(),
            linkPolicy.Sanitize(NearbyLinks.TransitDirections(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude)),
            linkPolicy.Sanitize(NearbyLinks.TransitDirections(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude)
                .Replace("travelmode=transit", "travelmode=driving")),
            linkPolicy.Sanitize(NearbyLinks.Uber(request.FromLatitude, request.FromLongitude, fromName, request.ToLatitude, request.ToLongitude, toName)));

        // In a catalog city, each ride also says which ticket to buy and where.
        if (await NearestGuideAsync(request.FromLatitude, request.FromLongitude, cancellationToken) is { } guide)
        {
            var language = T.CurrentLanguage;
            direction = direction with
            {
                Itineraries = direction.Itineraries.Select(i => i with
                {
                    Legs = i.Legs.Select(l => l.Mode == "WALK" ? l : l with { Ticket = GetStayRouteHandler.TicketFor(l, guide, language) }).ToList()
                }).ToList(),
                CityTicket = GetStayRouteHandler.TicketFor(new TransitLegDto("BUS", null, null, "", "", "", "", 0, null, null, null), guide, language)
            };
        }

        return new JourneyDto(fromName, toName, origin?.TimeZone, origin?.CountryCode,
            NearbyLinks.DistanceMeters(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude),
            direction, router.IsConfigured ? router.Name : null,
            IntercityStations.For(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude, T.CurrentLanguage));
    }

    public async Task<LiveVehiclesDto> Handle(LiveVehiclesQuery request, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        if (request.MaxLatitude - request.MinLatitude > MaxSpanDegrees || request.MaxLongitude - request.MinLongitude > MaxSpanDegrees)
            return new LiveVehiclesDto(now.ToUnixTimeSeconds(), true, [], live.IsConfigured ? live.Name : null);

        // Rounded to ~1 km and 15 s so viewers of the same area share the cached upstream answer.
        var box = new GeoBox(Math.Floor(request.MinLatitude * 100) / 100, Math.Floor(request.MinLongitude * 100) / 100,
            Math.Ceiling(request.MaxLatitude * 100) / 100, Math.Ceiling(request.MaxLongitude * 100) / 100);
        var from = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / 15 * 15);
        var segments = await live.VehiclesAsync(box, from.AddSeconds(-15), from.AddMinutes(2), cancellationToken);
        var vehicles = segments
            .GroupBy(s => s.TripId)
            .Select(g =>
            {
                var first = g.First();
                return new VehicleDto(g.Key, first.Mode, first.Line, first.Color, g.Any(s => s.RealTime),
                    g.OrderBy(s => s.Departure)
                        .Select(s => new VehicleStretchDto(s.Departure.ToUnixTimeSeconds(), s.Arrival.ToUnixTimeSeconds(), s.ToStop, s.Polyline))
                        .ToList());
            })
            .ToList();
        return new LiveVehiclesDto(now.ToUnixTimeSeconds(), false, vehicles, live.IsConfigured ? live.Name : null);
    }

    public async Task<TaxiOptionsDto> Handle(LocalTaxisQuery request, CancellationToken cancellationToken)
    {
        var country = request.CountryCode?.ToUpperInvariant()
                      ?? (await live.ReverseAsync(request.Latitude, request.Longitude, cancellationToken))?.CountryCode?.ToUpperInvariant();
        var found = await taxis.FindAsync(request.Latitude, request.Longitude, 6000, cancellationToken);
        var uber = request.ToLatitude is { } toLat && request.ToLongitude is { } toLon
            ? linkPolicy.Sanitize(NearbyLinks.Uber(request.Latitude, request.Longitude, "—", toLat, toLon, "—"))
            : null;
        var apps = RideHailingApps.For(country)
            .Select(a => new TaxiAppDto(a.Name, NearbyLinks.CuratedHttps(a.Url)!, a.Name == "Uber" ? uber : null))
            .Where(a => a.Url is not null)
            .ToList();
        var local = found.Select(t => new LocalTaxiDto(t.Name, t.Operator, t.Phone, t.Website,
            NearbyLinks.DistanceMeters(request.Latitude, request.Longitude, t.Latitude, t.Longitude), NearbyLinks.MapPoint(t.Latitude, t.Longitude), t.IsCompany)).ToList();
        var c = CultureInfo.InvariantCulture;
        var search = $"https://www.google.com/maps/search/taxi/@{request.Latitude.ToString("0.#####", c)},{request.Longitude.ToString("0.#####", c)},14z";
        return new TaxiOptionsDto(country, apps, local, search, "OpenStreetMap");
    }

    private async Task<TransitGuide?> NearestGuideAsync(double lat, double lon, CancellationToken ct)
    {
        // A degree box first (cheap in SQL), then the exact distance.
        var near = await db.Destinations.AsNoTracking()
            .Where(d => d.IsActive && d.Latitude > lat - 0.5 && d.Latitude < lat + 0.5 && d.Longitude > lon - 0.7 && d.Longitude < lon + 0.7)
            .Select(d => new { d.Code, d.Latitude, d.Longitude })
            .ToListAsync(ct);
        var best = near.Select(d => (d.Code, Meters: NearbyLinks.DistanceMeters(lat, lon, d.Latitude, d.Longitude)))
            .Where(d => d.Meters <= GuideRadiusMeters)
            .OrderBy(d => d.Meters)
            .FirstOrDefault();
        return best.Code is null ? null : guides.Get(best.Code);
    }

    private static PlaceSuggestionDto ToDto(GeocodedPlace p) => new(p.Name, p.Area, p.CountryCode, p.Latitude, p.Longitude, p.Kind);

    private static TimeZoneInfo? Zone(string? id)
    {
        if (id is null) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }

    private static string Label(string? name) => name?.Trim() is { Length: > 0 } n ? n : "—";

    private static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
