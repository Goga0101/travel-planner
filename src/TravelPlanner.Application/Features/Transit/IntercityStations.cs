using TravelPlanner.Application.Features.TripPlans;

namespace TravelPlanner.Application.Features.Transit;

/// <summary>A town served from a coach / minibus station, with its centre (to match the visitor's destination).</summary>
public sealed record IntercityTown(string NameEn, string NameKa, double Latitude, double Longitude);

/// <summary>A city's coach and minibus (marshrutka) station and the directions its vehicles leave for.</summary>
public sealed record IntercityStation(string NameEn, string NameKa, string MapQuery, IReadOnlyList<IntercityTown> Towns);

/// <summary>Which station to go to, the towns it serves and map links (the station by name, and getting there from the start).</summary>
public sealed record CoachStationDto(string Name, IReadOnlyList<string> Towns, bool Recommended, string MapUrl, string DirectionsUrl);

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
        new("Didube bus station", "დიდუბის ავტოსადგური", "Didube bus station, Tbilisi",
        [
            new("Mtskheta", "მცხეთა", 41.845, 44.718), new("Gori", "გორი", 41.984, 44.113), new("Borjomi", "ბორჯომი", 41.839, 43.383),
            new("Akhaltsikhe", "ახალციხე", 41.639, 42.982), new("Kutaisi", "ქუთაისი", 42.269, 42.705), new("Zugdidi", "ზუგდიდი", 42.508, 41.870),
            new("Ozurgeti", "ოზურგეთი", 41.924, 42.006), new("Poti", "ფოთი", 42.146, 41.672), new("Batumi", "ბათუმი", 41.643, 41.636),
            new("Stepantsminda", "სტეფანწმინდა", 42.657, 44.643),
        ]),
        new("Samgori (Navtlughi) bus station", "სამგორის (ნავთლუღის) ავტოსადგური", "Navtlughi bus station, Tbilisi",
        [
            new("Sagarejo", "საგარეჯო", 41.734, 45.330), new("Telavi", "თელავი", 41.919, 45.473), new("Gurjaani", "გურჯაანი", 41.743, 45.800),
            new("Sighnaghi", "სიღნაღი", 41.620, 45.922), new("Kvareli", "ყვარელი", 41.950, 45.809), new("Lagodekhi", "ლაგოდეხი", 41.826, 46.277),
            new("Dedoplistskaro", "დედოფლისწყარო", 41.465, 46.105),
        ]),
        new("Ortachala bus station", "ორთაჭალის ავტოსადგური", "Ortachala bus station, Tbilisi",
        [
            new("Yerevan", "ერევანი", 40.177, 44.513), new("Istanbul", "სტამბოლი", 41.008, 28.978),
        ]),
    ];

    /// <summary>The stations for a trip that starts in Tbilisi and leaves the city; empty otherwise. The best match comes first.</summary>
    public static IReadOnlyList<CoachStationDto> For(double fromLat, double fromLon, double toLat, double toLon, string language)
    {
        if (NearbyLinks.DistanceMeters(fromLat, fromLon, Tbilisi.Latitude, Tbilisi.Longitude) > CityRadiusMeters ||
            NearbyLinks.DistanceMeters(fromLat, fromLon, toLat, toLon) < MinTripMeters) return [];
        var ka = language == "ka";
        return TbilisiStations
            .Select(s => (Station: s, Nearest: s.Towns.Min(t => NearbyLinks.DistanceMeters(toLat, toLon, t.Latitude, t.Longitude))))
            .OrderBy(x => x.Nearest)
            .Select((x, i) => new CoachStationDto(
                ka ? x.Station.NameKa : x.Station.NameEn,
                x.Station.Towns.Select(t => ka ? t.NameKa : t.NameEn).ToList(),
                i == 0 && x.Nearest <= MatchMeters,
                NearbyLinks.MapSearch(x.Station.MapQuery),
                $"https://www.google.com/maps/dir/?api=1&origin={C(fromLat)},{C(fromLon)}&destination={Uri.EscapeDataString(x.Station.MapQuery)}&travelmode=transit"))
            .ToList();

        static string C(double v) => v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }
}
