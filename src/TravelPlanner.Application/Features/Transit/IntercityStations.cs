using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.Transit;

/// <summary>A town served from a coach / minibus station, with its centre (to match the visitor's destination).</summary>
public sealed record IntercityTown(string NameEn, string NameKa, double Latitude, double Longitude);

/// <summary>A city's coach and minibus (marshrutka) station (position from OpenStreetMap) and the directions its vehicles leave for.</summary>
public sealed record IntercityStation(string NameEn, string NameKa, string MapQuery, double Latitude, double Longitude, IReadOnlyList<IntercityTown> Towns);

/// <summary>
/// Which station to go to, the towns it serves and map links (the station by name, and getting there from the start).
/// Route: public transport from the start to the station, filled in for the recommended one.
/// </summary>
public sealed record CoachStationDto(string Name, IReadOnlyList<string> Towns, bool Recommended, string MapUrl, string DirectionsUrl,
    double Latitude, double Longitude, RouteDirectionDto? Route = null);

/// <summary>
/// Georgia's intercity minibuses are not in any open timetable, so journeys between regions only find trains. This hand-made
/// guide says which Tbilisi station serves which direction; vehicles usually leave when full, so times and fares are not given
/// (the UI says to check at the station).
/// </summary>
public static class IntercityStations
{
    private static readonly (double Latitude, double Longitude) Tbilisi = (41.7151, 44.8271);
    private const int CityRadiusMeters = 20_000;
    /// <summary>Destinations closer than this are city trips (the journey planner covers them).</summary>
    private const int MinTripMeters = 25_000;
    /// <summary>The recommended station serves a town within this distance of the destination.</summary>
    private const int MatchMeters = 30_000;

    public static readonly IReadOnlyList<IntercityStation> TbilisiStations =
    [
        new("Didube bus station", "დიდუბის ავტოსადგური", "Didube bus station, Tbilisi", 41.7518, 44.7791,
        [
            new("Mtskheta", "მცხეთა", 41.845, 44.718), new("Gori", "გორი", 41.984, 44.113), new("Borjomi", "ბორჯომი", 41.839, 43.383),
            new("Akhaltsikhe", "ახალციხე", 41.639, 42.982), new("Kutaisi", "ქუთაისი", 42.269, 42.705), new("Zugdidi", "ზუგდიდი", 42.508, 41.870),
            new("Ozurgeti", "ოზურგეთი", 41.924, 42.006), new("Poti", "ფოთი", 42.146, 41.672), new("Batumi", "ბათუმი", 41.643, 41.636),
            new("Stepantsminda", "სტეფანწმინდა", 42.657, 44.643),
        ]),
        new("Samgori (Navtlughi) bus station", "სამგორის (ნავთლუღის) ავტოსადგური", "Samgori bus station, Tbilisi", 41.6860, 44.8519,
        [
            new("Sagarejo", "საგარეჯო", 41.734, 45.330), new("Telavi", "თელავი", 41.919, 45.473), new("Gurjaani", "გურჯაანი", 41.743, 45.800),
            new("Sighnaghi", "სიღნაღი", 41.620, 45.922), new("Kvareli", "ყვარელი", 41.950, 45.809), new("Lagodekhi", "ლაგოდეხი", 41.826, 46.277),
            new("Dedoplistskaro", "დედოფლისწყარო", 41.465, 46.105),
        ]),
        new("Ortachala bus station", "ორთაჭალის ავტოსადგური", "Ortachala bus station, Tbilisi", 41.6756, 44.8343,
        [
            new("Yerevan", "ერევანი", 40.177, 44.513), new("Istanbul", "სტამბოლი", 41.008, 28.978),
        ]),
    ];

    /// <summary>A point within this distance of a served town's centre is in that town (Mtskheta is only ~17 km from Tbilisi's centre).</summary>
    private const int TownRadiusMeters = 8_000;

    /// <summary>The served town at a point and the Tbilisi station its minibuses use, or null.</summary>
    public static (IntercityStation Station, IntercityTown Town)? TownAt(double lat, double lon) =>
        TbilisiStations
            .SelectMany(s => s.Towns.Select(t => (Station: s, Town: t, Meters: NearbyLinks.DistanceMeters(lat, lon, t.Latitude, t.Longitude))))
            .Where(x => x.Meters <= TownRadiusMeters)
            .OrderBy(x => x.Meters)
            .Select(x => ((IntercityStation, IntercityTown)?)(x.Station, x.Town))
            .FirstOrDefault();

    public static bool InTbilisi(double lat, double lon) =>
        TownAt(lat, lon) is null && NearbyLinks.DistanceMeters(lat, lon, Tbilisi.Latitude, Tbilisi.Longitude) <= CityRadiusMeters;

    /// <summary>Minibuses between regions: no timetable, they leave when full; ~45 km/h near the city, ~48 km/h on long runs (road ×1.3, mountain roads).</summary>
    public const int MinibusWaitMinutes = 10;

    /// <summary>
    /// Intercity minibuses and buses without a timetable run in the daytime only (assumed ~07:00–19:30; the last ones to the
    /// regions usually leave in the early evening). Outside it the trip waits for the morning, and a taxi is offered instead.
    /// </summary>
    private static readonly TimeSpan FirstDeparture = new(7, 0, 0);
    private static readonly TimeSpan LastDeparture = new(19, 30, 0);

    public static bool InService(DateTime t) => t.TimeOfDay >= FirstDeparture && t.TimeOfDay <= LastDeparture;

    /// <summary>The time itself in the daytime, otherwise the next morning's first departure.</summary>
    public static DateTime NextService(DateTime t) =>
        InService(t) ? t : t.TimeOfDay < FirstDeparture ? t.Date + FirstDeparture : t.Date.AddDays(1) + FirstDeparture;

    public static string ServiceNote(bool night, string language, bool minibus) => (night, language == "ka") switch
    {
        (true, true) => "ღამით არ დადის · პირველი რეისი დილით, ჩვეულებრივ ~07:00-ზე · ღამით ტაქსი უფრო მოსახერხებელია",
        (true, false) => "Does not run at night · first departure in the morning, usually ~07:00 · at night a taxi is easier",
        (false, true) => minibus ? "მარშრუტკა · გადის, როცა შეივსება" : "საქალაქთაშორისო ავტობუსი / მარშრუტკა · გასვლის დრო სადგურზე დააზუსტეთ",
        (false, false) => minibus ? "Marshrutka · leaves when full" : "Intercity bus / minibus · check departures at the station"
    };

    public static TransitLegDto MinibusLeg(string fromName, double fromLat, double fromLon, string toName, double toLat, double toLon,
        DateTime start, string language)
    {
        var meters = NearbyLinks.DistanceMeters(fromLat, fromLon, toLat, toLon);
        var minutes = Math.Max(10, (int)Math.Round(meters * 1.3 / (meters > 50_000 ? 800 : 750)));
        var depart = NextService(start);
        var hm = (DateTime t) => t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        return new TransitLegDto("MINIBUS", null, toName, fromName, toName, hm(depart), hm(depart.AddMinutes(minutes)), minutes,
            ServiceNote(depart != start, language, minibus: true), null, null,
            Path: new LegPath(fromLat, fromLon, toLat, toLon, null, 0), WaitsForMorning: depart != start);
    }

    /// <summary>The stations for a trip that starts in Tbilisi and leaves the city; empty otherwise. The best match comes first.</summary>
    public static IReadOnlyList<CoachStationDto> For(double fromLat, double fromLon, double toLat, double toLon, string language)
    {
        if (!InTbilisi(fromLat, fromLon) ||
            (TownAt(toLat, toLon) is null && NearbyLinks.DistanceMeters(fromLat, fromLon, toLat, toLon) < MinTripMeters)) return [];
        var ka = language == "ka";
        return TbilisiStations
            .Select(s => (Station: s, Nearest: s.Towns.Min(t => NearbyLinks.DistanceMeters(toLat, toLon, t.Latitude, t.Longitude))))
            .OrderBy(x => x.Nearest)
            .Select((x, i) => new CoachStationDto(
                ka ? x.Station.NameKa : x.Station.NameEn,
                x.Station.Towns.Select(t => ka ? t.NameKa : t.NameEn).ToList(),
                i == 0 && x.Nearest <= MatchMeters,
                NearbyLinks.MapPoint(x.Station.Latitude, x.Station.Longitude),
                NearbyLinks.TransitDirections(fromLat, fromLon, x.Station.Latitude, x.Station.Longitude),
                x.Station.Latitude, x.Station.Longitude))
            .ToList();

    }
}
