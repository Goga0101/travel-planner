using System.Globalization;
using System.Text;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.Transit;

public sealed record MetroStation(string NameEn, string NameKa, double Latitude, double Longitude);

/// <summary>A metro line: stations in order and the minutes between consecutive ones (same both ways).</summary>
public sealed record MetroLine(string Name, string Color, IReadOnlyList<MetroStation> Stations, IReadOnlyList<int> Minutes);

/// <summary>Where two lines meet: the station index on each and the walk between the platforms.</summary>
public sealed record MetroTransfer(int LineA, int StationA, int LineB, int StationB, int WalkMinutes);

public sealed record CityMetro(string City, double Latitude, double Longitude, IReadOnlyList<MetroLine> Lines, IReadOnlyList<MetroTransfer> Transfers);

/// <summary>
/// Metro networks modelled here because the open timetable is incomplete: Tbilisi's feed has only one direction of each line,
/// so the journey planner never rides the metro towards the centre. Stations and run times come from that feed
/// (Transitous, October 2026); the reverse direction is assumed to take the same time.
/// </summary>
public static class CityMetros
{
    private static MetroStation S(string en, string ka, double lat, double lon) => new(en, ka, lat, lon);

    public static readonly IReadOnlyList<CityMetro> All =
    [
        new("Tbilisi", 41.7151, 44.8271,
        [
            new("1", "e2231a",
            [
                S("Akhmeteli Theatre", "ახმეტელის თეატრი", 41.79207, 44.81591), S("Sarajishvili", "სარაჯიშვილი", 41.78359, 44.79952),
                S("Guramishvili", "გურამიშვილი", 41.77509, 44.79524), S("Grmaghele", "ღრმაღელე", 41.76560, 44.79029),
                S("Didube", "დიდუბე", 41.74977, 44.77999), S("Gotsiridze", "გოცირიძე", 41.74295, 44.78401),
                S("Nadzaladevi", "ნაძალადევი", 41.73265, 44.79635), S("Station Square", "სადგურის მოედანი", 41.72210, 44.79774),
                S("Marjanishvili", "მარჯანიშვილი", 41.70993, 44.79751), S("Rustaveli", "რუსთაველი", 41.70414, 44.78961),
                S("Liberty Square", "თავისუფლების მოედანი", 41.69500, 44.79992), S("Avlabari", "ავლაბარი", 41.69258, 44.81522),
                S("300 Aragveli", "300 არაგველი", 41.68784, 44.82711), S("Isani", "ისანი", 41.68663, 44.84084),
                S("Samgori", "სამგორი", 41.68602, 44.85569), S("Varketili", "ვარკეთილი", 41.69159, 44.86989),
            ],
            [3, 2, 2, 4, 1, 3, 2, 3, 1, 3, 2, 2, 2, 3, 2]),
            new("2", "009b48",
            [
                S("Station Square", "სადგურის მოედანი", 41.72250, 44.79577), S("Tsereteli", "წერეთელი", 41.72701, 44.78783),
                S("Technical University", "ტექნიკური უნივერსიტეტი", 41.72123, 44.77509), S("Medical University", "სამედიცინო უნივერსიტეტი", 41.72731, 44.76284),
                S("Delisi", "დელისი", 41.72547, 44.74465), S("Vazha-Pshavela", "ვაჟა-ფშაველა", 41.72427, 44.73083),
                S("State University", "სახელმწიფო უნივერსიტეტი", 41.72278, 44.71705),
            ],
            [1, 2, 2, 3, 3, 2]),
        ],
        [new MetroTransfer(0, 7, 1, 0, 4)]),
    ];

    /// <summary>A station counts as near when it is at most this far (about 20 minutes on foot).</summary>
    private const int NearMeters = 1500;
    private const int CityRadiusMeters = 30_000;
    private const int FirstWaitMinutes = 4;
    private const int TransferWaitMinutes = 3;

    /// <summary>
    /// The quickest metro journey between two points (walk, ride with at most one change, walk), or null when there is no metro,
    /// no station within reach of both ends, or walking the whole way is as quick. Times are estimates (no departure times).
    /// </summary>
    public static TransitItineraryDto? Route(double fromLat, double fromLon, string fromName, double toLat, double toLon, string toName,
        DateTime departure, string language)
    {
        var metro = All.FirstOrDefault(m => NearbyLinks.DistanceMeters(fromLat, fromLon, m.Latitude, m.Longitude) <= CityRadiusMeters);
        if (metro is null) return null;
        var near = Stations(metro, fromLat, fromLon).ToList();
        var far = Stations(metro, toLat, toLon).ToList();

        (int Total, List<Ride> Rides, int WalkIn, int WalkOut, MetroStation Board, MetroStation Alight)? best = null;
        foreach (var a in near)
        foreach (var b in far)
        {
            if (Plan(metro, a.Line, a.Index, b.Line, b.Index) is not { } rides || rides.Count == 0) continue;
            var total = a.WalkMinutes + FirstWaitMinutes + rides.Sum(r => r.Minutes + r.TransferMinutes) + b.WalkMinutes;
            if (best is null || total < best.Value.Total)
                best = (total, rides, a.WalkMinutes, b.WalkMinutes, metro.Lines[a.Line].Stations[a.Index], metro.Lines[b.Line].Stations[b.Index]);
        }
        if (best is not { } x || x.Total >= WalkMinutes(fromLat, fromLon, toLat, toLon)) return null;

        string N(MetroStation s) => language == "ka" ? s.NameKa : s.NameEn;
        var legs = new List<TransitLegDto>();
        var t = departure;
        legs.Add(Leg("WALK", null, null, fromName, N(x.Board), ref t, x.WalkIn, new LegPath(fromLat, fromLon, x.Board.Latitude, x.Board.Longitude, null, 0)));
        t = t.AddMinutes(FirstWaitMinutes);
        foreach (var ride in x.Rides)
        {
            if (ride.TransferMinutes > 0)
            {
                var (p, q) = (ride.Stations[0], legs[^1].Path!);
                legs.Add(Leg("WALK", null, null, legs[^1].To, N(p), ref t, ride.TransferMinutes - TransferWaitMinutes,
                    new LegPath(q.ToLatitude, q.ToLongitude, p.Latitude, p.Longitude, null, 0)));
                t = t.AddMinutes(TransferWaitMinutes);
            }
            var first = ride.Stations[0];
            var last = ride.Stations[^1];
            legs.Add(Leg("SUBWAY", ride.Line.Name, N(ride.Towards), N(first), N(last), ref t, ride.Minutes,
                new LegPath(first.Latitude, first.Longitude, last.Latitude, last.Longitude, Encode(ride.Stations), 5), ride.Stations.Count - 1, ride.Line.Color));
        }
        legs.Add(Leg("WALK", null, null, N(x.Alight), toName, ref t, x.WalkOut, new LegPath(x.Alight.Latitude, x.Alight.Longitude, toLat, toLon, null, 0)));
        return new TransitItineraryDto((int)(t - departure).TotalMinutes, x.Rides.Count - 1, Hm(departure), Hm(t), legs, Estimated: true);
    }

    /// <summary>Slower metro options are still offered (some prefer the metro), but not when they take this much longer.</summary>
    private const int MaxSlowerMinutes = 15;

    /// <summary>
    /// The journeys found plus the metro option when it is not already among them and not much slower than the quickest,
    /// quickest first.
    /// </summary>
    public static List<TransitItineraryDto> WithMetro(IReadOnlyList<TransitItineraryDto> options, double fromLat, double fromLon, string fromName,
        double toLat, double toLon, string toName, DateTime departure, string language)
    {
        var list = options.ToList();
        if (Route(fromLat, fromLon, fromName, toLat, toLon, toName, departure, language) is not { } metro) return list;
        var quickest = list.Count > 0 ? list.Min(i => i.DurationMinutes) : int.MaxValue;
        var sameRide = list.Any(i => i.Legs.Count(l => l.Mode != "WALK") == metro.Legs.Count(l => l.Mode != "WALK") &&
                                     i.Legs.Where(l => l.Mode != "WALK").All(l => l.Mode is "SUBWAY" or "METRO"));
        if (!sameRide && metro.DurationMinutes <= quickest + MaxSlowerMinutes) list.Add(metro);
        return list.OrderBy(i => i.DurationMinutes).ToList();
    }

    private sealed record Ride(MetroLine Line, List<MetroStation> Stations, MetroStation Towards, int Minutes, int TransferMinutes);

    private static IEnumerable<(int Line, int Index, int WalkMinutes)> Stations(CityMetro metro, double lat, double lon) =>
        metro.Lines.SelectMany((l, li) => l.Stations.Select((s, si) => (Line: li, Index: si,
                Meters: NearbyLinks.DistanceMeters(lat, lon, s.Latitude, s.Longitude))))
            .Where(s => s.Meters <= NearMeters)
            .OrderBy(s => s.Meters)
            .Take(4)
            .Select(s => (s.Line, s.Index, Math.Max(1, (int)Math.Round(s.Meters * 1.3 / 80))));

    /// <summary>Same line: one ride. Different lines: ride to the transfer station, change, ride on.</summary>
    private static List<Ride>? Plan(CityMetro metro, int lineA, int a, int lineB, int b)
    {
        if (lineA == lineB) return a == b ? [] : [RideOn(metro.Lines[lineA], a, b, 0)];
        var tr = metro.Transfers.FirstOrDefault(x => (x.LineA == lineA && x.LineB == lineB) || (x.LineA == lineB && x.LineB == lineA));
        if (tr is null) return null;
        var (fromIdx, toIdx) = tr.LineA == lineA ? (tr.StationA, tr.StationB) : (tr.StationB, tr.StationA);
        var rides = new List<Ride>();
        if (a != fromIdx) rides.Add(RideOn(metro.Lines[lineA], a, fromIdx, 0));
        if (b != toIdx) rides.Add(RideOn(metro.Lines[lineB], toIdx, b, rides.Count > 0 ? tr.WalkMinutes + TransferWaitMinutes : 0));
        return rides;
    }

    private static Ride RideOn(MetroLine line, int from, int to, int transferMinutes)
    {
        var step = from < to ? 1 : -1;
        var stations = new List<MetroStation>();
        var minutes = 0;
        for (var i = from; ; i += step)
        {
            stations.Add(line.Stations[i]);
            if (i == to) break;
            minutes += line.Minutes[Math.Min(i, i + step)];
        }
        var towards = step > 0 ? line.Stations[^1] : line.Stations[0];
        return new Ride(line, stations, towards, minutes, transferMinutes);
    }

    private static TransitLegDto Leg(string mode, string? line, string? headsign, string from, string to, ref DateTime t, int minutes, LegPath path,
        int? stops = null, string? color = null)
    {
        var start = t;
        t = t.AddMinutes(minutes);
        return new TransitLegDto(mode, line, headsign, from, to, Hm(start), Hm(t), minutes, null, stops, color, Path: path);
    }

    private static int WalkMinutes(double aLat, double aLon, double bLat, double bLon) =>
        (int)Math.Round(NearbyLinks.DistanceMeters(aLat, aLon, bLat, bLon) * 1.3 / 80);

    /// <summary>Google encoded polyline (precision 5) through the stations.</summary>
    public static string Encode(IEnumerable<MetroStation> stations)
    {
        var sb = new StringBuilder();
        int pLat = 0, pLon = 0;
        foreach (var s in stations)
        {
            int lat = (int)Math.Round(s.Latitude * 1e5), lon = (int)Math.Round(s.Longitude * 1e5);
            Put(lat - pLat);
            Put(lon - pLon);
            (pLat, pLon) = (lat, lon);
        }
        return sb.ToString();

        void Put(int v)
        {
            var u = v < 0 ? ~(v << 1) : v << 1;
            while (u >= 0x20)
            {
                sb.Append((char)((0x20 | (u & 0x1f)) + 63));
                u >>= 5;
            }
            sb.Append((char)(u + 63));
        }
    }

    private static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
