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
/// DrivingMinutes and WalkingMinutes (null when too far to walk) are rough estimates from the distance, so every trip says something.
/// FlightsUrl is set for distances people usually fly.
/// </summary>
public sealed record JourneyDto(
    string FromName, string ToName, string? TimeZone, string? CountryCode, int DistanceMeters, RouteDirectionDto Direction, string? RouterName,
    IReadOnlyList<CoachStationDto> Coaches, int DrivingMinutes = 0, int? WalkingMinutes = null, string? FlightsUrl = null);

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
    IStationFinder stations,
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
        var language = T.CurrentLanguage;
        var airports = await AirportsAsync(request.Text, language, cancellationToken);
        var places = await live.GeocodeAsync(request.Text, language, request.NearLatitude, request.NearLongitude, cancellationToken);
        return airports.Concat(places.Select(ToDto)).ToList();
    }

    /// <summary>"airport" in the languages visitors type it in (Georgian "აეროპორტი", "თბილისის აეროპორტში", Russian "аэропорт" …).</summary>
    private static readonly string[] AirportWords = ["airport", "აეროპორტ", "аэропорт", "flughafen", "aeroport", "aéroport", "aeropuerto", "aeroporto", "havalimanı"];

    /// <summary>
    /// Arriving travellers start at an airport: its code ("TBS") or "&lt;city&gt; airport" in any language puts the airport first, at the
    /// terminal, rather than a street or a bus stop of that name (the map has "თბილისის აეროპორტი" under no name).
    /// </summary>
    private async Task<IReadOnlyList<PlaceSuggestionDto>> AirportsAsync(string text, string language, CancellationToken ct)
    {
        var term = text.Trim();
        var upper = term.ToUpperInvariant();
        if (term.Length == 3 && term.All(char.IsAsciiLetter))
            return (await db.Airports.AsNoTracking().Where(a => a.IataCode == upper).ToListAsync(ct)).Select(a => Airport(a, language)).ToList();

        var word = AirportWords.FirstOrDefault(w => term.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (word is null) return [];
        // The rest is the city, possibly inflected ("თბილისის", "Тбилиси"): compare by its stem.
        var city = string.Join(' ', term.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.Contains(word, StringComparison.OrdinalIgnoreCase) && !w.Equals("international", StringComparison.OrdinalIgnoreCase)));
        if (city.Length < 3) return [];
        var stem = city[..Math.Max(3, city.Length - 2)];
        // Translated names live in a JSON column: other scripts are matched in memory, as the airport selector does.
        var candidates = term.Any(c => c > 0x7F)
            ? (await db.Airports.AsNoTracking().ToListAsync(ct)).Where(a => a.Translations.Count > 0).ToList()
            : await db.Airports.AsNoTracking().Where(a => a.City.ToLower().StartsWith(stem.ToLower())).Take(50).ToListAsync(ct);
        return candidates
            .Where(a => a.CityIn(language).StartsWith(stem, StringComparison.OrdinalIgnoreCase) ||
                        a.City.StartsWith(stem, StringComparison.OrdinalIgnoreCase) ||
                        a.Translations.Values.Any(t => t.City?.StartsWith(stem, StringComparison.OrdinalIgnoreCase) == true))
            .OrderByDescending(a => a.Translations.Count > 0).ThenBy(a => a.IataCode)
            .Take(3).Select(a => Airport(a, language)).ToList();
    }

    private static PlaceSuggestionDto Airport(Domain.Entities.Airport a, string language) =>
        new($"{a.NameIn(language)} ({a.IataCode})", a.CityIn(language), a.CountryCode, a.Latitude, a.Longitude, "airport");

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
        var guide = await NearestGuideAsync(request.FromLatitude, request.FromLongitude, cancellationToken);
        var direction = await DirectionAsync(request.FromLatitude, request.FromLongitude, fromName, request.ToLatitude, request.ToLongitude, toName,
            wished, offset, guide, cancellationToken);

        // To another region without published timetables: how to reach the minibus station for that direction.
        var coaches = IntercityStations.For(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude, T.CurrentLanguage).ToList();
        if (coaches.FindIndex(c => c.Recommended) is var i and >= 0)
        {
            var station = coaches[i];
            coaches[i] = station with
            {
                Route = await DirectionAsync(request.FromLatitude, request.FromLongitude, fromName, station.Latitude, station.Longitude, station.Name,
                    wished, offset, guide, cancellationToken)
            };
        }

        // Minibus + city transport, both ways: to a region via its Tbilisi station, or from a regional town into the city.
        var language = T.CurrentLanguage;
        var combined = new List<TransitItineraryDto>();
        if (coaches.FirstOrDefault(c => c.Recommended)?.Route is { } toStation && IntercityStations.TownAt(request.ToLatitude, request.ToLongitude) is not null)
        {
            var station = coaches.First(c => c.Recommended);
            foreach (var city in toStation.Itineraries.Take(2))
            {
                var board = At(wished, city.ArrivalTime).AddMinutes(IntercityStations.MinibusWaitMinutes);
                combined.Add(Join(wished, city.Legs,
                    [IntercityStations.MinibusLeg(station.Name, station.Latitude, station.Longitude, toName, request.ToLatitude, request.ToLongitude, board, language)],
                    city.Transfers + 1));
            }
            // No city connection in the open data (Tbilisi airport has none): a taxi to the station, then the minibus.
            if (toStation.Itineraries.Count == 0)
            {
                var taxi = TaxiLeg(fromName, request.FromLatitude, request.FromLongitude, station.Name, station.Latitude, station.Longitude, wished, language);
                var board = wished.AddMinutes(taxi.DurationMinutes + IntercityStations.MinibusWaitMinutes);
                combined.Add(Join(wished, [taxi],
                    [IntercityStations.MinibusLeg(station.Name, station.Latitude, station.Longitude, toName, request.ToLatitude, request.ToLongitude, board, language)], 1));
            }
        }
        else if (IntercityStations.TownAt(request.FromLatitude, request.FromLongitude) is { } home &&
                 IntercityStations.InTbilisi(request.ToLatitude, request.ToLongitude))
        {
            var s = home.Station;
            var stationName = language == "ka" ? s.NameKa : s.NameEn;
            var minibus = IntercityStations.MinibusLeg(fromName, request.FromLatitude, request.FromLongitude, stationName, s.Latitude, s.Longitude,
                wished.AddMinutes(IntercityStations.MinibusWaitMinutes), language);
            // From the minibus's own times: at night it only leaves in the morning.
            var arrive = At(wished, minibus.ArrivalTime);
            var inCity = await DirectionAsync(s.Latitude, s.Longitude, stationName, request.ToLatitude, request.ToLongitude, toName,
                arrive.AddMinutes(3), offset, null, cancellationToken);
            foreach (var city in inCity.Itineraries.Take(2)) combined.Add(Join(wished, [minibus], city.Legs, city.Transfers + 1));
            if (inCity.Itineraries.Count == 0)
                combined.Add(Join(wished, [minibus],
                    [TaxiLeg(stationName, s.Latitude, s.Longitude, toName, request.ToLatitude, request.ToLongitude, arrive.AddMinutes(3), language)], 1));
        }
        var distance = NearbyLinks.DistanceMeters(request.FromLatitude, request.FromLongitude, request.ToLatitude, request.ToLongitude);
        // Anywhere else: between towns with no timetable connection, the bus stations OpenStreetMap knows at both ends.
        if (combined.Count == 0 && distance is >= IntercityMinMeters and <= IntercityMaxMeters && direction.Itineraries.All(i => i.Estimated))
        {
            if (await ViaBusStationsAsync(request.FromLatitude, request.FromLongitude, fromName, request.ToLatitude, request.ToLongitude, toName,
                    wished, offset, guide, cancellationToken) is { } coach)
                combined.Add(coach);
        }
        // A minibus or bus that has to wait for the morning (hours longer than driving): at night a taxi all the way is realistic.
        if (combined.Any(i => i.DurationMinutes > DrivingMinutes(distance) + 6 * 60))
            combined.Add(Join(wished, [], [TaxiLeg(fromName, request.FromLatitude, request.FromLongitude, toName, request.ToLatitude, request.ToLongitude,
                wished, language, night: true)], 0));
        if (combined.Count > 0)
            direction = direction with { Itineraries = direction.Itineraries.Concat(combined).OrderBy(i => i.DurationMinutes).ToList() };

        return new JourneyDto(fromName, toName, origin?.TimeZone, origin?.CountryCode, distance,
            direction, router.IsConfigured ? router.Name : null, coaches, DrivingMinutes(distance), distance <= MaxWalkSummaryMeters ? WalkMinutes(distance) : null,
            distance >= FlightMinMeters ? FlightsUrl(fromName, toName) : null);
    }

    /// <summary>Beyond this one bus estimate from station to station is not credible (borders, changes, days on the road).</summary>
    private const int IntercityMaxMeters = 600_000;
    /// <summary>From this distance people usually fly; the page suggests it.</summary>
    private const int FlightMinMeters = 500_000;

    private static string FlightsUrl(string from, string to) =>
        "https://www.google.com/travel/flights?q=" + Uri.EscapeDataString($"Flights from {from} to {to}");

    /// <summary>Trips this long without a timetable connection look for intercity buses.</summary>
    private const int IntercityMinMeters = 15_000;
    private const int StationRadiusKm = 8;
    /// <summary>Intercity buses rarely have published times here; a typical wait at the station.</summary>
    private const int CoachWaitMinutes = 15;
    private const int MaxWalkSummaryMeters = 15_000;

    /// <summary>
    /// Start → (city transport or a walk) → the bus station near the start → intercity bus → the bus station near the destination
    /// → (city transport or a walk) → destination. A missing station at one end means the bus is boarded or left at the point itself.
    /// </summary>
    private async Task<TransitItineraryDto?> ViaBusStationsAsync(double fromLat, double fromLon, string fromName, double toLat, double toLon,
        string toName, DateTime wished, TimeSpan offset, TransitGuide? guide, CancellationToken ct)
    {
        var near = stations.BusStationNearAsync(fromLat, fromLon, StationRadiusKm, ct);
        var far = stations.BusStationNearAsync(toLat, toLon, StationRadiusKm, ct);
        var (a, b) = (await near, await far);
        if (a is null && b is null) return null;
        if (a is not null && b is not null && NearbyLinks.DistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude) < 5_000) return null;

        var language = T.CurrentLanguage;
        var legs = new List<TransitLegDto>();
        var transfers = 0;
        var t = wished;
        var (boardLat, boardLon, boardName) = a is null ? (fromLat, fromLon, fromName) : (a.Latitude, a.Longitude, a.Name);
        if (a is not null && NearbyLinks.DistanceMeters(fromLat, fromLon, a.Latitude, a.Longitude) > 300)
        {
            var part = await DirectionAsync(fromLat, fromLon, fromName, a.Latitude, a.Longitude, a.Name, t, offset, guide, ct);
            var legsIn = part.Itineraries.FirstOrDefault()?.Legs ?? [WalkLeg(fromName, fromLat, fromLon, a.Name, a.Latitude, a.Longitude, t)];
            legs.AddRange(legsIn);
            transfers += legsIn.Count(l => l.Mode != "WALK");
            t = At(t, legs[^1].ArrivalTime);
        }

        var (alightLat, alightLon, alightName) = b is null ? (toLat, toLon, toName) : (b.Latitude, b.Longitude, b.Name);
        var wait = t.AddMinutes(CoachWaitMinutes);
        var start = IntercityStations.NextService(wait); // no night buses without a timetable
        var minutes = Math.Max(10, (int)Math.Round(NearbyLinks.DistanceMeters(boardLat, boardLon, alightLat, alightLon) * 1.3 / 1000)); // ~60 km/h
        legs.Add(new TransitLegDto("COACH", null, alightName, boardName, alightName, Hm(start), Hm(start.AddMinutes(minutes)), minutes,
            IntercityStations.ServiceNote(start != wait, language, minibus: false),
            null, null, Path: new LegPath(boardLat, boardLon, alightLat, alightLon, null, 0), WaitsForMorning: start != wait));
        t = start.AddMinutes(minutes);

        if (b is not null && NearbyLinks.DistanceMeters(b.Latitude, b.Longitude, toLat, toLon) > 300)
        {
            var part = await DirectionAsync(b.Latitude, b.Longitude, b.Name, toLat, toLon, toName, t.AddMinutes(3), offset, guide, ct);
            var legsOut = part.Itineraries.FirstOrDefault()?.Legs ?? [WalkLeg(b.Name, b.Latitude, b.Longitude, toName, toLat, toLon, t)];
            legs.AddRange(legsOut);
            transfers += legsOut.Count(l => l.Mode != "WALK");
            // Times are clock times; count from the bus's arrival so a journey past midnight stays right.
            t = At(t, legs[^1].ArrivalTime);
        }
        var end = t;
        return new TransitItineraryDto((int)(end - wished).TotalMinutes, transfers, Hm(wished), legs[^1].ArrivalTime, legs, Estimated: true);
    }

    /// <summary>A taxi / ride-hailing hop where the open data has no public transport (timed like a city drive).</summary>
    /// <param name="night">The whole way by car because the minibus or bus would only leave in the morning.</param>
    private static TransitLegDto TaxiLeg(string fromName, double fromLat, double fromLon, string toName, double toLat, double toLon, DateTime start,
        string language, bool night = false)
    {
        var minutes = DrivingMinutes(NearbyLinks.DistanceMeters(fromLat, fromLon, toLat, toLon));
        var note = (night, language == "ka") switch
        {
            (true, true) => "ტაქსი ან Bolt / Yandex Go პირდაპირ · მარშრუტკები ღამით არ დადის, ასე დილამდე ლოდინი არ მოგიწევთ",
            (true, false) => "Taxi or ride-hailing all the way · minibuses do not run at night, so no waiting until morning",
            (false, true) => "ტაქსი ან Bolt / Yandex Go · საზოგადოებრივი ტრანსპორტი აქ ვერ მოიძებნა",
            (false, false) => "Taxi or ride-hailing · no public transport found here",
        };
        return new TransitLegDto("TAXI", null, toName, fromName, toName, Hm(start), Hm(start.AddMinutes(minutes)), minutes, note,
            null, null, Path: new LegPath(fromLat, fromLon, toLat, toLon, null, 0));
    }

    private static TransitLegDto WalkLeg(string fromName, double fromLat, double fromLon, string toName, double toLat, double toLon, DateTime start)
    {
        var minutes = WalkMinutes(NearbyLinks.DistanceMeters(fromLat, fromLon, toLat, toLon));
        return new TransitLegDto("WALK", null, null, fromName, toName, Hm(start), Hm(start.AddMinutes(minutes)), minutes, null, null, null,
            Path: new LegPath(fromLat, fromLon, toLat, toLon, null, 0));
    }

    /// <summary>Roads are ~1.3× the straight line; ~25 km/h in town, ~60 km/h between towns.</summary>
    private static int DrivingMinutes(int meters) => Math.Max(1, (int)Math.Round(meters * 1.3 / (meters < IntercityMinMeters ? 420 : 1000)));

    private static int WalkMinutes(int meters) => Math.Max(1, (int)Math.Round(meters * 1.3 / 80));

    /// <summary>
    /// Timetable journeys between two points (intercity trains included), else rides along lines OpenStreetMap lists at stops
    /// near both ends; in a catalog city each ride also says which ticket to buy and where.
    /// </summary>
    private async Task<RouteDirectionDto> DirectionAsync(double fromLat, double fromLon, string fromName, double toLat, double toLon, string toName,
        DateTime wished, TimeSpan offset, TransitGuide? guide, CancellationToken ct)
    {
        var itineraries = router.IsConfigured
            ? await router.PlanAsync(new TransitRouteQuery(fromLat, fromLon, toLat, toLon, wished, offset, AnyDistance: true), ct)
            : [];
        var timetableDate = itineraries.Count > 0 ? DateOnly.FromDateTime(itineraries[0].DepartureLocal) : DateOnly.FromDateTime(wished);
        var estimated = itineraries.Count > 0 ? [] : await StopLineRoutes.FindAsync(stops, fromLat, fromLon, fromName, toLat, toLon, toName, wished, ct);
        var found = itineraries.Select(i => new TransitItineraryDto(i.DurationMinutes, i.Transfers, Hm(i.DepartureLocal), Hm(i.ArrivalLocal),
            i.Legs.Select(l => new TransitLegDto(l.Mode, l.Line, l.Headsign, l.From, l.To, Hm(l.DepartureLocal), Hm(l.ArrivalLocal),
                l.DurationMinutes, l.Agency, l.Stops, l.Color, Path: l.Path)).ToList())).Concat(estimated).ToList();
        var direction = new RouteDirectionDto(fromName, toName, timetableDate, timetableDate == DateOnly.FromDateTime(wished),
            wished.ToString("HH:mm", CultureInfo.InvariantCulture),
            // The metro where the open timetable misses it (Tbilisi has only one direction of each line).
            CityMetros.WithMetro(found, fromLat, fromLon, fromName, toLat, toLon, toName, wished, T.CurrentLanguage),
            linkPolicy.Sanitize(NearbyLinks.TransitDirections(fromLat, fromLon, toLat, toLon)),
            linkPolicy.Sanitize(NearbyLinks.TransitDirections(fromLat, fromLon, toLat, toLon).Replace("travelmode=transit", "travelmode=driving")),
            linkPolicy.Sanitize(NearbyLinks.Uber(fromLat, fromLon, fromName, toLat, toLon, toName)));
        if (guide is null) return direction;

        var language = T.CurrentLanguage;
        return direction with
        {
            Itineraries = direction.Itineraries.Select(i => i with
            {
                Legs = i.Legs.Select(l => l.Mode == "WALK" ? l : l with { Ticket = GetStayRouteHandler.TicketFor(l, guide, language) }).ToList()
            }).ToList(),
            CityTicket = GetStayRouteHandler.TicketFor(new TransitLegDto("BUS", null, null, "", "", "", "", 0, null, null, null), guide, language)
        };
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

    /// <summary>One journey from two parts (the minibus has no timetable, so the whole is an estimate).</summary>
    private static TransitItineraryDto Join(DateTime start, IReadOnlyList<TransitLegDto> first, IReadOnlyList<TransitLegDto> second, int transfers)
    {
        var legs = first.Concat(second).ToList();
        var end = At(start, legs[^1].ArrivalTime);
        return new TransitItineraryDto((int)(end - start).TotalMinutes, transfers, Hm(start), legs[^1].ArrivalTime, legs, Estimated: true);
    }

    /// <summary>"HH:mm" as a moment on or after the reference (journeys may pass midnight).</summary>
    private static DateTime At(DateTime reference, string hm)
    {
        if (!TimeOnly.TryParseExact(hm, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) return reference;
        var at = reference.Date.Add(t.ToTimeSpan());
        return at < reference.AddMinutes(-1) ? at.AddDays(1) : at;
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
