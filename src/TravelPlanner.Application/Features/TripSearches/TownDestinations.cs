using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Application.Features.TripSearches;

/// <summary>
/// A town or village picked on the map (no airport of its own): "town|lat|lon|CC|name|country", the names in the language it
/// was searched in (the country part is optional).
/// </summary>
public sealed record TownPick(string Name, string CountryCode, double Latitude, double Longitude, string? Country = null)
{
    public const string Prefix = "town|";

    public string Code => string.Create(CultureInfo.InvariantCulture,
        $"{Prefix}{Latitude:0.0000}|{Longitude:0.0000}|{CountryCode}|{Clean(Name)}") + (Country is { Length: > 0 } c ? "|" + Clean(c) : "");

    private static string Clean(string text) => text.Replace('|', ' ').Trim();

    public static TownPick? Parse(string? code)
    {
        if (code is null || !code.StartsWith(Prefix, StringComparison.Ordinal) || code.Length > 160) return null;
        var parts = code[Prefix.Length..].Split('|', 5);
        if (parts.Length < 4 ||
            !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) || lat is < -90 or > 90 ||
            !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon) || lon is < -180 or > 180 ||
            parts[2].Length != 2 || string.IsNullOrWhiteSpace(parts[3])) return null;
        return new TownPick(parts[3].Trim(), parts[2].ToUpperInvariant(), lat, lon, parts.Length > 4 && parts[4].Trim() is { Length: > 0 } c ? c : null);
    }
}

/// <summary>Airports that serve a town without one, and loading them into read-only destinations.</summary>
public static class GatewayAirports
{
    /// <summary>Farther than this, flying there is not the way to reach the town.</summary>
    public const int MaxKm = 250;
    private const int MaxGateways = 2;

    /// <summary>The nearest flightable airports, the town's own country first (Mestia: Kutaisi, not Nalchik across the border).</summary>
    public static async Task<List<(Airport Airport, int Km)>> FindAsync(IApplicationDbContext db, double lat, double lon, string countryCode,
        CancellationToken ct)
    {
        const double degree = 111.0;
        var dLat = MaxKm / degree;
        var dLon = MaxKm / (degree * Math.Max(0.2, Math.Cos(lat * Math.PI / 180)));
        var box = await db.Airports.AsNoTracking()
            .Where(a => a.IsDepartureOption && a.Latitude > lat - dLat && a.Latitude < lat + dLat && a.Longitude > lon - dLon && a.Longitude < lon + dLon)
            .ToListAsync(ct);
        var near = box
            .Select(a => (Airport: a, Km: (int)Math.Round(Features.TripPlans.NearbyLinks.DistanceMeters(lat, lon, a.Latitude, a.Longitude) / 1000.0)))
            .Where(x => x.Km <= MaxKm)
            .ToList();
        // With an airport in the town's own country, only those: Sighnaghi flies via Tbilisi, not Ganja across the border.
        var home = near.Where(x => string.Equals(x.Airport.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase)).ToList();
        return (home.Count > 0 ? home : near).OrderBy(x => x.Km).Take(MaxGateways).ToList();
    }

    /// <summary>
    /// Puts the gateway airports of airport-less towns into Airports, so planning and plan pages treat them like any destination.
    /// Only for untracked destinations: on a tracked one EF would move the airport to this destination on save.
    /// </summary>
    public static async Task LoadAsync(IApplicationDbContext db, IEnumerable<Destination> destinations, CancellationToken ct)
    {
        var towns = destinations.Where(d => d.Airports.Count == 0 && d.GatewayAirports.Count > 0).ToList();
        if (towns.Count == 0) return;
        var codes = towns.SelectMany(d => d.GatewayAirports).Distinct().ToList();
        var airports = await db.Airports.AsNoTracking().Where(a => codes.Contains(a.IataCode)).ToListAsync(ct);
        foreach (var town in towns)
            town.Airports.AddRange(town.GatewayAirports.Select(c => airports.FirstOrDefault(a => a.IataCode == c)).OfType<Airport>());
    }
}
