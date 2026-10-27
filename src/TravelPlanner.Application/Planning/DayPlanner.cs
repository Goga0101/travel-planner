using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Providers;

namespace TravelPlanner.Application.Planning;

public enum TripPace { Relaxed, Normal, Intense }

public enum PlanDayKind { Arrival, Full, Departure }

public sealed record DayPlanRequest(
    DateOnly FirstDay,
    DateOnly LastDay,
    DateTime? ArrivalLocal,
    DateTime? DepartureLocal,
    IReadOnlyList<string> Interests,
    TripPace Pace,
    double CentreLatitude,
    double CentreLongitude);

/// <summary>A suggested place for a slot, with the walking distance from the previous stop of the day.</summary>
public sealed record PlannedPlace(ActivityPlace Place, int DistanceMeters, double FromLatitude, double FromLongitude);

public sealed record PlannedItem(string Slot, TimeOnly Time, PlannedPlace? Place, CityEvent? Event, IReadOnlyList<PlannedPlace> Alternatives);

public sealed record PlannedDay(int Number, DateOnly Date, PlanDayKind Kind, IReadOnlyList<PlannedItem> Items);

/// <summary>
/// Builds a day-by-day itinerary from places and events, deterministically:
/// - every day has slots (morning, lunch, afternoon, dinner, night …) that depend on the pace and the interests;
/// - the arrival day starts two hours after landing and the departure day ends 3.5 hours before take-off;
/// - the first daytime slot of a day takes the most notable unused place of that day's interest, and every later slot
///   the closest unused place of its interest to the previous stop, so a day stays walkable;
/// - an event on a day (matching the interests) is placed at its start time;
/// - every slot carries a few alternatives so the visitor can swap without asking the server again.
/// </summary>
public static class DayPlanner
{
    public const int AlternativesPerSlot = 3;
    private static readonly TimeSpan AfterLanding = TimeSpan.FromHours(2);
    private static readonly TimeSpan BeforeTakeOff = TimeSpan.FromHours(3.5);
    /// <summary>Notable places count as this much closer when choosing the next stop.</summary>
    private const int NotableBonusMeters = 700;

    private sealed record Slot(string Name, TimeOnly Time, IReadOnlyList<string> Interests, bool Anchor = false);

    public static IReadOnlyList<PlannedDay> Build(DayPlanRequest request, IReadOnlyList<ActivityPlace> places, IReadOnlyList<CityEvent> events)
    {
        var interests = request.Interests.Count > 0 ? request.Interests : [Interests.Culture, Interests.Food];
        var daytime = Interests.Daytime.Where(interests.Contains).ToList();
        if (daytime.Count == 0) daytime = [Interests.Culture];

        var used = new HashSet<string>();
        var usedEvents = new HashSet<string>();
        var days = new List<PlannedDay>();
        var number = 0;
        for (var date = request.FirstDay; date <= request.LastDay; date = date.AddDays(1))
        {
            number++;
            var kind = date == request.FirstDay ? PlanDayKind.Arrival : date == request.LastDay ? PlanDayKind.Departure : PlanDayKind.Full;
            var slots = SlotsFor(number - 1, interests, daytime, request.Pace);

            // Arrival and departure days only keep the slots that fit around the flights.
            if (kind == PlanDayKind.Arrival && request.ArrivalLocal is { } landing && DateOnly.FromDateTime(landing) == date)
            {
                var start = TimeOnly.FromDateTime(landing.Add(AfterLanding));
                slots = landing.Add(AfterLanding).Date > landing.Date ? [] : slots.Where(s => s.Time >= start).ToList();
            }
            else if (kind == PlanDayKind.Arrival && request.ArrivalLocal is { } later && DateOnly.FromDateTime(later) > date)
            {
                slots = []; // still travelling
            }
            if (kind == PlanDayKind.Departure && request.DepartureLocal is { } takeOff && DateOnly.FromDateTime(takeOff) == date)
            {
                var end = takeOff.Subtract(BeforeTakeOff);
                slots = end.Date < takeOff.Date ? [] : slots.Where(s => s.Time.ToTimeSpan() + TimeSpan.FromHours(1.5) <= end.TimeOfDay).ToList();
            }

            var items = new List<PlannedItem>();
            var (lat, lon) = (request.CentreLatitude, request.CentreLongitude);

            var dayEvent = events
                .Where(e => e.Date == date && !usedEvents.Contains(e.Id) && EventMatches(e, interests))
                .OrderBy(e => e.Time ?? new TimeOnly(20, 0))
                .FirstOrDefault();
            var eventTime = dayEvent?.Time ?? new TimeOnly(20, 0);
            var eventPlaced = false;

            foreach (var slot in slots)
            {
                if (dayEvent is not null && !eventPlaced && eventTime <= slot.Time)
                {
                    items.Add(new PlannedItem("event", eventTime, null, dayEvent, []));
                    usedEvents.Add(dayEvent.Id);
                    eventPlaced = true;
                    if (dayEvent.Latitude is { } eLat && dayEvent.Longitude is { } eLon) (lat, lon) = (eLat, eLon);
                    // An evening event replaces the slot it overlaps.
                    if (slot.Time.ToTimeSpan() < eventTime.ToTimeSpan() + TimeSpan.FromHours(2)) continue;
                }

                var ranked = Rank(places, slot, used, lat, lon);
                if (ranked.Count == 0) continue;
                var chosen = ranked[0];
                used.Add(chosen.Place.Id);
                items.Add(new PlannedItem(slot.Name, slot.Time, chosen, null, ranked.Skip(1).Take(AlternativesPerSlot).ToList()));
                (lat, lon) = (chosen.Place.Latitude, chosen.Place.Longitude);
            }

            if (dayEvent is not null && !eventPlaced && (kind != PlanDayKind.Departure || slots.Count > 0))
            {
                items.Add(new PlannedItem("event", eventTime, null, dayEvent, []));
                usedEvents.Add(dayEvent.Id);
            }

            days.Add(new PlannedDay(number, date, kind, items.OrderBy(i => i.Time).ToList()));
        }
        return days;
    }

    private static List<Slot> SlotsFor(int dayIndex, IReadOnlyList<string> interests, IReadOnlyList<string> daytime, TripPace pace)
    {
        var morning = daytime[dayIndex % daytime.Count];
        var afternoon = daytime.Count > 1 ? daytime[(dayIndex + 1) % daytime.Count] : morning;
        var meal = interests.Contains(Interests.Food) ? (IReadOnlyList<string>)[Interests.Food] : [Interests.Food, Interests.Cafes];
        var night = new[] { Interests.Bars, Interests.Nightlife }.Where(interests.Contains).ToList();

        var slots = new List<Slot>();
        if (interests.Contains(Interests.Cafes)) slots.Add(new Slot("breakfast", new TimeOnly(9, 0), [Interests.Cafes]));
        switch (pace)
        {
            case TripPace.Relaxed:
                slots.Add(new Slot("morning", new TimeOnly(10, 30), [morning], Anchor: true));
                slots.Add(new Slot("lunch", new TimeOnly(13, 30), meal));
                slots.Add(new Slot("dinner", new TimeOnly(19, 30), meal));
                break;
            case TripPace.Intense:
                slots.Add(new Slot("morning", new TimeOnly(9, 30), [morning], Anchor: true));
                slots.Add(new Slot("lateMorning", new TimeOnly(11, 30), [morning]));
                slots.Add(new Slot("lunch", new TimeOnly(13, 30), meal));
                slots.Add(new Slot("afternoon", new TimeOnly(15, 0), [afternoon]));
                slots.Add(new Slot("lateAfternoon", new TimeOnly(17, 0), [afternoon]));
                slots.Add(new Slot("dinner", new TimeOnly(20, 0), meal));
                break;
            default:
                slots.Add(new Slot("morning", new TimeOnly(10, 0), [morning], Anchor: true));
                slots.Add(new Slot("lunch", new TimeOnly(13, 0), meal));
                slots.Add(new Slot("afternoon", new TimeOnly(15, 0), [afternoon]));
                slots.Add(new Slot("dinner", new TimeOnly(19, 30), meal));
                break;
        }
        if (interests.Contains(Interests.Shows) && dayIndex % 2 == 1) slots.Add(new Slot("show", new TimeOnly(21, 0), [Interests.Shows]));
        if (night.Count > 0)
        {
            // Bars and clubs alternate when both are chosen; clubs open late.
            var pick = night[dayIndex % night.Count];
            slots.Add(new Slot("night", pick == Interests.Nightlife ? new TimeOnly(23, 0) : new TimeOnly(22, 0), [pick]));
        }
        return slots.OrderBy(s => s.Time).ToList();
    }

    /// <summary>Anchors follow the source order (sights come ranked by fame); other slots the closest place, notable ones counted closer.</summary>
    private static List<PlannedPlace> Rank(IReadOnlyList<ActivityPlace> places, Slot slot, HashSet<string> used, double lat, double lon) =>
        places
            .Select((p, index) => (Place: p, Index: index))
            .Where(x => slot.Interests.Contains(x.Place.Interest) && !used.Contains(x.Place.Id))
            .Select(x => (Planned: new PlannedPlace(x.Place, NearbyLinks.DistanceMeters(lat, lon, x.Place.Latitude, x.Place.Longitude), lat, lon), x.Index))
            .OrderBy(x => slot.Anchor ? (x.Planned.Place.Notable ? 0 : 1) : 0)
            .ThenBy(x => slot.Anchor ? x.Index : x.Planned.DistanceMeters - (x.Planned.Place.Notable ? NotableBonusMeters : 0))
            .ThenBy(x => x.Planned.DistanceMeters)
            .ThenBy(x => x.Planned.Place.Id, StringComparer.Ordinal)
            .Select(x => x.Planned)
            .ToList();

    /// <summary>Events are scheduled when their kind fits the interests (music for bars/nightlife, arts and theatre for shows/culture).</summary>
    public static bool EventMatches(CityEvent e, IReadOnlyList<string> interests)
    {
        var segment = e.Segment ?? "";
        if (segment.Contains("Music", StringComparison.OrdinalIgnoreCase))
            return interests.Contains(Interests.Nightlife) || interests.Contains(Interests.Bars) || interests.Contains(Interests.Shows);
        if (segment.Contains("Arts", StringComparison.OrdinalIgnoreCase) || segment.Contains("Theatre", StringComparison.OrdinalIgnoreCase))
            return interests.Contains(Interests.Shows) || interests.Contains(Interests.Culture);
        return interests.Contains(Interests.Shows);
    }
}
