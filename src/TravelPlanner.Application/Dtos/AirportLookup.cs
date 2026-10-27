using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Dtos;

/// <summary>City names for the airport codes a response mentions (connections, departure and arrival), loaded in one query.</summary>
public sealed class AirportLookup
{
    public static readonly AirportLookup Empty = new(new Dictionary<string, Airport>());

    private readonly IReadOnlyDictionary<string, Airport> _airports;

    private AirportLookup(IReadOnlyDictionary<string, Airport> airports) => _airports = airports;

    public string? CityOf(string iata) => _airports.TryGetValue(iata, out var a) ? a.CityIn(T.CurrentLanguage) : null;

    public ViaStopDto Stop(string iata) =>
        _airports.TryGetValue(iata, out var a) ? new ViaStopDto(iata, a.CityIn(T.CurrentLanguage), a.CountryCode) : new ViaStopDto(iata, iata, null);

    public static async Task<AirportLookup> LoadAsync(IApplicationDbContext db, IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var wanted = codes.Where(c => c is { Length: 3 }).Select(c => c.ToUpperInvariant()).Distinct().ToList();
        if (wanted.Count == 0) return Empty;
        var airports = await db.Airports.AsNoTracking().Where(a => wanted.Contains(a.IataCode)).ToListAsync(cancellationToken);
        return new AirportLookup(airports.ToDictionary(a => a.IataCode));
    }

    public static IEnumerable<string> CodesOf(FlightItinerary itinerary) =>
        [itinerary.DepartureAirport, itinerary.ArrivalAirport, .. itinerary.ViaAirports];

    /// <summary>Airport codes in the flight items of the given plans.</summary>
    public static IEnumerable<string> CodesOf(IEnumerable<TripPlan> plans) =>
        plans.SelectMany(p => p.Items)
            .Where(i => i.Type == PlanItemType.Flight)
            .Select(i => Deserialize(i.DetailsJson))
            .Where(d => d is not null)
            .SelectMany(d => CodesOf(d!.Outbound).Concat(CodesOf(d.Inbound)));

    private static FlightItemDetails? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<FlightItemDetails>(json, AppJson.Options); }
        catch (JsonException) { return null; }
    }
}
