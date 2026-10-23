using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Planning;

public static class FlightFilter
{
    /// <summary>
    /// Removes offers that violate the user's preferences. Unknown values (e.g. baggage not reported by the
    /// provider) are not treated as violations; they are surfaced as notes instead.
    /// </summary>
    public static IReadOnlyList<FlightOffer> Apply(IEnumerable<FlightOffer> offers, TripPreferences preferences, DateTimeOffset nowUtc)
    {
        return offers.Where(o => Matches(o, preferences, nowUtc)).ToList();
    }

    public static bool Matches(FlightOffer offer, TripPreferences preferences, DateTimeOffset nowUtc)
    {
        if (offer.TotalPrice <= 0) return false;
        if (offer.ExpiresAtUtc.HasValue && offer.ExpiresAtUtc.Value <= nowUtc) return false;
        if (preferences.MaxStops.HasValue && offer.MaxStops > preferences.MaxStops.Value) return false;

        if (preferences.MaxFlightDurationHours is { } maxHours)
        {
            var limit = TimeSpan.FromHours(maxHours);
            if (offer.Outbound.Duration > limit || offer.Inbound.Duration > limit) return false;
        }

        if (preferences.IncludeCheckedBaggage && offer.IncludedCheckedBags == 0) return false;

        if (preferences.PreferredDepartureTime != DepartureTimePreference.Any &&
            !InWindow(offer.Outbound.DepartureLocal, preferences.PreferredDepartureTime))
            return false;

        return true;
    }

    public static bool InWindow(DateTime localTime, DepartureTimePreference preference)
    {
        var h = localTime.Hour;
        return preference switch
        {
            DepartureTimePreference.Morning => h is >= 5 and < 12,
            DepartureTimePreference.Afternoon => h is >= 12 and < 17,
            DepartureTimePreference.Evening => h is >= 17 and < 22,
            DepartureTimePreference.Night => h >= 22 || h < 5,
            _ => true
        };
    }
}
