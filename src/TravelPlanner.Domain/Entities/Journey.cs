using TravelPlanner.Domain.Common;

namespace TravelPlanner.Domain.Entities;

/// <summary>
/// A trip a signed-in traveller booked from one of our plans. During the trip they tick off each day; when every day is
/// ticked the journey is completed and counts on the leaderboard. A day can only be ticked once it has begun, so a trip
/// cannot be "completed" before it happened.
/// </summary>
public class Journey : Entity
{
    private Journey() { }

    public Journey(Guid userId, Guid tripPlanId, string destinationCode, string city, string countryCode,
        DateOnly startDate, DateOnly endDate, DateTime nowUtc)
    {
        if (endDate < startDate) throw new ArgumentException("The trip ends before it starts.", nameof(endDate));
        UserId = userId;
        TripPlanId = tripPlanId;
        DestinationCode = destinationCode;
        City = city;
        CountryCode = countryCode;
        StartDate = startDate;
        EndDate = endDate;
        BookedAtUtc = nowUtc;
    }

    public Guid UserId { get; private set; }
    public Guid TripPlanId { get; private set; }
    // Copied from the plan's destination so the leaderboard needs no joins.
    public string DestinationCode { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    public DateTime BookedAtUtc { get; private set; }
    public List<DateOnly> CompletedDays { get; private set; } = [];
    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>Every calendar day of the trip, arrival and departure day included.</summary>
    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;
    public bool IsCompleted => CompletedAtUtc is not null;

    /// <summary>Why a day cannot be ticked (or null when it can).</summary>
    public string? CannotMark(DateOnly day, DateOnly today) =>
        day < StartDate || day > EndDate ? "journey.dayOutsideTrip"
        : day > today ? "journey.dayInFuture"
        : null;

    /// <summary>Ticks or unticks one day; the journey completes when all days are ticked (and reopens when one is unticked).</summary>
    public void SetDay(DateOnly day, bool done, DateOnly today, DateTime nowUtc)
    {
        if (CannotMark(day, today) is { } reason) throw new InvalidOperationException(reason);
        if (done && !CompletedDays.Contains(day)) CompletedDays = [.. CompletedDays, day];
        if (!done) CompletedDays = CompletedDays.Where(d => d != day).ToList();
        CompletedDays.Sort();
        CompletedAtUtc = CompletedDays.Count == DayCount ? CompletedAtUtc ?? nowUtc : null;
    }
}
