using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Domain.Entities;

public class TripPreferences
{
    public bool IncludeCheckedBaggage { get; set; }
    public int? MinAccommodationStars { get; set; }
    public int? MaxFlightDurationHours { get; set; }
    public int? MaxStops { get; set; }
    public DepartureTimePreference PreferredDepartureTime { get; set; } = DepartureTimePreference.Any;
    public bool IncludeDailyExpenseEstimates { get; set; }
    /// <summary>Also combine separate one-way tickets through a connecting city (often cheaper, but unprotected connections).</summary>
    public bool AllowSelfTransfer { get; set; } = true;
}
