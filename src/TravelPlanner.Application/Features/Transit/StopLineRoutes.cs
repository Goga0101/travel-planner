using System.Globalization;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Features.Transit;

/// <summary>
/// Journeys for cities whose operators publish no timetables (Athens buses, many regions): a line that calls at a stop near
/// the start and at a stop near the destination, from the lines OpenStreetMap lists per stop. There are no departure times,
/// so the itinerary is marked Estimated and its times are rough (walks at 80 m/min, a typical speed per kind of vehicle,
/// plus a wait).
/// </summary>
public static class StopLineRoutes
{
    private const int StopRadiusMeters = 600;
    /// <summary>Shorter trips are walked.</summary>
    private const int MinRideMeters = 700;
    private const int MaxOptions = 3;
    private const int WaitMinutes = 6;

    public static async Task<List<TransitItineraryDto>> FindAsync(ITransitStopSource stops, double fromLat, double fromLon, string fromName,
        double toLat, double toLon, string toName, DateTime departure, CancellationToken ct)
    {
        if (!stops.IsConfigured || NearbyLinks.DistanceMeters(fromLat, fromLon, toLat, toLon) < MinRideMeters) return [];
        var near = stops.FindAsync(fromLat, fromLon, StopRadiusMeters, ct);
        var far = stops.FindAsync(toLat, toLon, StopRadiusMeters, ct);
        return Build(await near, await far, fromLat, fromLon, fromName, toLat, toLon, toName, departure);
    }

    public static List<TransitItineraryDto> Build(IReadOnlyList<NearbyTransitStop> near, IReadOnlyList<NearbyTransitStop> far,
        double fromLat, double fromLon, string fromName, double toLat, double toLon, string toName, DateTime departure)
    {
        var candidates = new List<(string? Line, NearbyTransitStop Board, NearbyTransitStop Alight, int Walk)>();
        foreach (var a in near)
        foreach (var b in far)
        {
            if (a.Mode != b.Mode || NearbyLinks.DistanceMeters(a.Latitude, a.Longitude, b.Latitude, b.Longitude) < MinRideMeters / 2) continue;
            var walk = Walk(fromLat, fromLon, a.Latitude, a.Longitude) + Walk(b.Latitude, b.Longitude, toLat, toLon);
            var common = a.Lines.Intersect(b.Lines, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var line in common) candidates.Add((line, a, b, walk));
            // Metro and rail stations are often tagged without lines; two different stations are still one ride apart.
            if (common.Count == 0 && a.Mode is "subway" or "train" or "light_rail" && a.Name != b.Name) candidates.Add((null, a, b, walk));
        }

        return candidates
            .GroupBy(c => (c.Line ?? c.Board.Name + ">" + c.Alight.Name).ToLowerInvariant())
            .Select(g => g.OrderBy(c => c.Walk).First())
            .OrderBy(c => c.Walk)
            .Take(MaxOptions)
            .Select(c => Itinerary(c.Line, c.Board, c.Alight, fromLat, fromLon, fromName, toLat, toLon, toName, departure))
            .OrderBy(i => i.DurationMinutes)
            .ToList();
    }

    private static TransitItineraryDto Itinerary(string? line, NearbyTransitStop board, NearbyTransitStop alight,
        double fromLat, double fromLon, string fromName, double toLat, double toLon, string toName, DateTime departure)
    {
        var mode = board.Mode switch { "subway" => "SUBWAY", "tram" => "TRAM", "train" => "RAIL", "light_rail" => "TRAM", _ => "BUS" };
        var walkIn = WalkMinutes(fromLat, fromLon, board.Latitude, board.Longitude);
        var ride = RideMinutes(mode, NearbyLinks.DistanceMeters(board.Latitude, board.Longitude, alight.Latitude, alight.Longitude));
        var walkOut = WalkMinutes(alight.Latitude, alight.Longitude, toLat, toLon);

        var t0 = departure;
        var t1 = t0.AddMinutes(walkIn);
        var t2 = t1.AddMinutes(WaitMinutes);
        var t3 = t2.AddMinutes(ride);
        var t4 = t3.AddMinutes(walkOut);
        var legs = new List<TransitLegDto>
        {
            new("WALK", null, null, fromName, board.Name, Hm(t0), Hm(t1), walkIn, null, null, null,
                Path: new LegPath(fromLat, fromLon, board.Latitude, board.Longitude, null, 0)),
            new(mode, line, null, board.Name, alight.Name, Hm(t2), Hm(t3), ride, null, null, null,
                Path: new LegPath(board.Latitude, board.Longitude, alight.Latitude, alight.Longitude, null, 0)),
            new("WALK", null, null, alight.Name, toName, Hm(t3), Hm(t4), walkOut, null, null, null,
                Path: new LegPath(alight.Latitude, alight.Longitude, toLat, toLon, null, 0)),
        };
        return new TransitItineraryDto((int)(t4 - t0).TotalMinutes, 0, Hm(t0), Hm(t4), legs, Estimated: true);
    }

    private static int Walk(double aLat, double aLon, double bLat, double bLon) => NearbyLinks.DistanceMeters(aLat, aLon, bLat, bLon);

    /// <summary>Streets are not straight (~1.3×) and people walk ~80 m a minute.</summary>
    private static int WalkMinutes(double aLat, double aLon, double bLat, double bLon) =>
        Math.Max(1, (int)Math.Round(Walk(aLat, aLon, bLat, bLon) * 1.3 / 80));

    /// <summary>Typical city speeds including stops: bus ~16 km/h, tram ~15, metro ~30, train ~40.</summary>
    private static int RideMinutes(string mode, int meters)
    {
        var metersPerMinute = mode switch { "SUBWAY" => 500, "RAIL" => 650, "TRAM" => 250, _ => 270 };
        return Math.Max(2, (int)Math.Round(meters * 1.3 / metersPerMinute));
    }

    private static string Hm(DateTime t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);
}
