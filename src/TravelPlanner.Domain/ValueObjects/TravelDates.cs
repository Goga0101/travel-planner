namespace TravelPlanner.Domain.ValueObjects;

/// <summary>
/// A concrete departure/return pair.
/// Semantics: a trip of N days covers N calendar days including both the departure day and the return day,
/// so it needs N - 1 accommodation nights (check-in on the departure day, check-out on the return day).
/// Example: a 5-day trip departing 20 Nov returns 24 Nov and needs 4 nights.
/// </summary>
public readonly record struct TravelDates(DateOnly Departure, DateOnly Return)
{
    public const int MinimumDurationDays = 2;
    public const int MaximumDurationDays = 30;

    public int DurationDays => Return.DayNumber - Departure.DayNumber + 1;
    public int Nights => Return.DayNumber - Departure.DayNumber;
    public DateOnly CheckIn => Departure;
    public DateOnly CheckOut => Return;

    public static TravelDates ForDuration(DateOnly departure, int durationDays)
    {
        if (durationDays < MinimumDurationDays)
            throw new ArgumentOutOfRangeException(nameof(durationDays), $"A trip must last at least {MinimumDurationDays} days.");
        return new TravelDates(departure, departure.AddDays(durationDays - 1));
    }

    public static int NightsForDuration(int durationDays) => durationDays - 1;

    public override string ToString() => $"{Departure:yyyy-MM-dd}..{Return:yyyy-MM-dd} ({Nights} nights)";
}
