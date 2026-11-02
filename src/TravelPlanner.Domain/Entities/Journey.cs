using TravelPlanner.Domain.Common;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Domain.Entities;

/// <summary>
/// A trip a signed-in traveller saved from one of our plans. It starts as a draft (with the hotel they picked); once they
/// confirm they bought it, they tick off each day during the trip, and when every day is ticked the journey is completed
/// and counts on the leaderboard. A day can only be ticked once it has begun, so a trip cannot be "completed" before it
/// happened.
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
        SavedAtUtc = nowUtc;
    }

    public Guid UserId { get; private set; }
    public Guid TripPlanId { get; private set; }
    // Copied from the plan's destination so the leaderboard needs no joins.
    public string DestinationCode { get; private set; } = null!;
    public string City { get; private set; } = null!;
    public string CountryCode { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly EndDate { get; private set; }
    /// <summary>Local departure time of the outbound flight, when the plan has one (for the "have a good flight" e-mail).</summary>
    public DateTime? DepartureLocal { get; set; }

    /// <summary>The hotel the traveller picked on the plan page (the plan itself only links to a search).</summary>
    public string? StayName { get; private set; }
    public double? StayLatitude { get; private set; }
    public double? StayLongitude { get; private set; }
    /// <summary>Language of the site when the trip was saved: the reminder e-mails are written in it ("ka" or "en").</summary>
    public string Lang { get; set; } = "ka";

    public JourneyStatus Status { get; private set; }
    public DateTime SavedAtUtc { get; private set; }
    public DateTime? BookedAtUtc { get; private set; }
    public List<DateOnly> CompletedDays { get; private set; } = [];
    public DateTime? CompletedAtUtc { get; private set; }

    public DateTime? FlightEmailSentAtUtc { get; private set; }
    public DateTime? StartEmailSentAtUtc { get; private set; }

    /// <summary>Every calendar day of the trip, arrival and departure day included.</summary>
    public int DayCount => EndDate.DayNumber - StartDate.DayNumber + 1;
    public bool IsCompleted => CompletedAtUtc is not null;

    public void SetStay(string? name, double? latitude, double? longitude)
    {
        var hasStay = !string.IsNullOrWhiteSpace(name) && latitude is not null && longitude is not null;
        StayName = hasStay ? name!.Trim() : null;
        StayLatitude = hasStay ? latitude : null;
        StayLongitude = hasStay ? longitude : null;
    }

    /// <summary>"I bought the tickets and the stay." Calling it again changes nothing.</summary>
    public void ConfirmBooked(DateTime nowUtc)
    {
        if (Status == JourneyStatus.Booked) return;
        Status = JourneyStatus.Booked;
        BookedAtUtc = nowUtc;
    }

    /// <summary>"I have not bought it after all": back to a draft; ticked days are forgotten.</summary>
    public void BackToDraft()
    {
        Status = JourneyStatus.Draft;
        BookedAtUtc = null;
        CompletedDays = [];
        CompletedAtUtc = null;
    }

    public void MarkFlightEmailSent(DateTime nowUtc) => FlightEmailSentAtUtc ??= nowUtc;
    public void MarkStartEmailSent(DateTime nowUtc) => StartEmailSentAtUtc ??= nowUtc;

    /// <summary>Why a day cannot be ticked (or null when it can).</summary>
    public string? CannotMark(DateOnly day, DateOnly today) =>
        Status != JourneyStatus.Booked ? "journey.notBooked"
        : day < StartDate || day > EndDate ? "journey.dayOutsideTrip"
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
