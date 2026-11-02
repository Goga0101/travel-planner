using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using TravelPlanner.Application.Common;
using TravelPlanner.Application.Features.TripPlans;
using TravelPlanner.Application.Planning;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Features.Journeys;

public sealed record JourneyDto(
    Guid TripPlanId, string DestinationCode, string City, string CountryCode, DateOnly StartDate, DateOnly EndDate, int DayCount,
    JourneyStatus Status, string? StayName, DateTime? DepartureLocal, IReadOnlyList<DateOnly> CompletedDays,
    DateTime SavedAtUtc, DateTime? BookedAtUtc, DateTime? CompletedAtUtc);

/// <summary>The hotel picked on the plan page and the site language (for the reminder e-mails), saved with the trip.</summary>
public sealed record SaveJourneyDetails(string? StayName, double? StayLatitude, double? StayLongitude, string? Lang);

/// <summary>
/// One traveller on the leaderboard: first name and the initial of the last name only. Sample entries (IsSample) are
/// illustrations shown until real travellers fill the board; the site labels them.
/// </summary>
public sealed record LeaderboardEntryDto(
    int Rank, string Name, int Trips, int Days, IReadOnlyList<string> Countries, IReadOnlyList<string> Cities,
    DateTime LastCompletedAtUtc, bool IsSample, bool IsYou);

public sealed record LeaderboardDto(IReadOnlyList<LeaderboardEntryDto> Entries, int RealTravellers);

/// <summary>Illustrative leaderboard rows (catalog/leaderboard-samples.json), shown with a "sample" label.</summary>
public sealed record LeaderboardSample(string Name, int Trips, int Days, IReadOnlyList<string> Countries, IReadOnlyList<string> Cities, DateTime LastCompletedAtUtc);

public interface ILeaderboardSamples
{
    IReadOnlyList<LeaderboardSample> Get();
}

public static class JourneyMapping
{
    public static JourneyDto ToDto(this Journey j) =>
        new(j.TripPlanId, j.DestinationCode, j.City, j.CountryCode, j.StartDate, j.EndDate, j.DayCount, j.Status, j.StayName, j.DepartureLocal,
            j.CompletedDays, j.SavedAtUtc, j.BookedAtUtc, j.CompletedAtUtc);

    public static void Apply(this Journey j, SaveJourneyDetails? details)
    {
        if (details is null) return;
        j.SetStay(details.StayName is { Length: > 200 } n ? n[..200] : details.StayName, details.StayLatitude, details.StayLongitude);
        if (details.Lang is "ka" or "en") j.Lang = details.Lang;
    }

    /// <summary>
    /// The latest calendar date anywhere on Earth (UTC+14): a traveller in Sydney or Auckland may tick their day while it
    /// is still yesterday in UTC.
    /// </summary>
    public static DateOnly LatestToday(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(14));

    /// <summary>"Nino Maisuradze" → "Nino M."; a single name stays as it is.</summary>
    public static string PublicName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return "?";
        var initial = parts[^1][0];
        // Georgian has no capital letters in ordinary writing (ToUpper would turn "ბ" into Mtavruli "Ბ").
        if (initial is < (char)0x10A0 or > (char)0x10FF) initial = char.ToUpperInvariant(initial);
        return parts.Length == 1 ? parts[0] : $"{parts[0]} {initial}.";
    }

    public static ValidationException Invalid(string property, string key) => new([new ValidationFailure(property, T.Get(key))]);
}

// ---- My journeys ----

public sealed record GetMyJourneysQuery(Guid UserId) : IRequest<IReadOnlyList<JourneyDto>>;

public sealed class GetMyJourneysHandler(IApplicationDbContext db) : IRequestHandler<GetMyJourneysQuery, IReadOnlyList<JourneyDto>>
{
    public async Task<IReadOnlyList<JourneyDto>> Handle(GetMyJourneysQuery request, CancellationToken cancellationToken)
    {
        var journeys = await db.Journeys.AsNoTracking().Where(j => j.UserId == request.UserId)
            .OrderByDescending(j => j.StartDate).ToListAsync(cancellationToken);
        return journeys.Select(j => j.ToDto()).ToList();
    }
}

/// <summary>
/// Saves the plan to the traveller's trips as a draft (with the picked hotel), or updates the saved hotel and language of
/// a trip already saved; its status stays as it is.
/// </summary>
public sealed record StartJourneyCommand(Guid UserId, Guid PlanId, SaveJourneyDetails? Details = null) : IRequest<JourneyDto>;

public sealed class StartJourneyHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<StartJourneyCommand, JourneyDto>
{
    public async Task<JourneyDto> Handle(StartJourneyCommand request, CancellationToken cancellationToken)
    {
        var existing = await db.Journeys.FirstOrDefaultAsync(j => j.UserId == request.UserId && j.TripPlanId == request.PlanId, cancellationToken);
        if (existing is not null)
        {
            existing.Apply(request.Details);
            await db.SaveChangesAsync(cancellationToken);
            return existing.ToDto();
        }
        if (!await db.Users.AnyAsync(u => u.Id == request.UserId, cancellationToken)) throw new NotFoundException("resource.user", request.UserId);

        var plan = await TripPlanLoader.LoadAsync(db, request.PlanId, tracking: false, cancellationToken)
                   ?? throw new NotFoundException("resource.tripPlan", request.PlanId);
        var d = plan.Destination!;
        var journey = new Journey(request.UserId, plan.Id, d.Code, d.City, d.CountryCode, plan.DepartureDate, plan.ReturnDate, clock.GetUtcNow().UtcDateTime)
        {
            DepartureLocal = OutboundDeparture(plan),
        };
        journey.Apply(request.Details);
        db.Journeys.Add(journey);
        await db.SaveChangesAsync(cancellationToken);
        return journey.ToDto();
    }

    private static DateTime? OutboundDeparture(TripPlan plan)
    {
        var flight = plan.Items.FirstOrDefault(i => i.Type == PlanItemType.Flight);
        if (flight is null) return null;
        try { return JsonSerializer.Deserialize<FlightItemDetails>(flight.DetailsJson, AppJson.Options)?.Outbound.DepartureLocal; }
        catch (JsonException) { return null; }
    }
}

/// <summary>"I bought the tickets and the stay" (Booked = true), or back to a draft (false, ticked days are forgotten).</summary>
public sealed record SetJourneyBookedCommand(Guid UserId, Guid PlanId, bool Booked) : IRequest<JourneyDto>;

public sealed class SetJourneyBookedHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<SetJourneyBookedCommand, JourneyDto>
{
    public async Task<JourneyDto> Handle(SetJourneyBookedCommand request, CancellationToken cancellationToken)
    {
        var journey = await db.Journeys.FirstOrDefaultAsync(j => j.UserId == request.UserId && j.TripPlanId == request.PlanId, cancellationToken)
                      ?? throw new NotFoundException("resource.journey", request.PlanId);
        if (request.Booked) journey.ConfirmBooked(clock.GetUtcNow().UtcDateTime);
        else journey.BackToDraft();
        await db.SaveChangesAsync(cancellationToken);
        return journey.ToDto();
    }
}

/// <summary>Removes the trip from the traveller's trips (with its ticked days).</summary>
public sealed record CancelJourneyCommand(Guid UserId, Guid PlanId) : IRequest;

public sealed class CancelJourneyHandler(IApplicationDbContext db) : IRequestHandler<CancelJourneyCommand>
{
    public async Task Handle(CancelJourneyCommand request, CancellationToken cancellationToken)
    {
        var journey = await db.Journeys.FirstOrDefaultAsync(j => j.UserId == request.UserId && j.TripPlanId == request.PlanId, cancellationToken);
        if (journey is null) return;
        db.Journeys.Remove(journey);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Ticks (or unticks) one day of a booked trip. Days that have not begun yet cannot be ticked.</summary>
public sealed record SetJourneyDayCommand(Guid UserId, Guid PlanId, DateOnly Day, bool Done) : IRequest<JourneyDto>;

public sealed class SetJourneyDayHandler(IApplicationDbContext db, TimeProvider clock) : IRequestHandler<SetJourneyDayCommand, JourneyDto>
{
    public async Task<JourneyDto> Handle(SetJourneyDayCommand request, CancellationToken cancellationToken)
    {
        var journey = await db.Journeys.FirstOrDefaultAsync(j => j.UserId == request.UserId && j.TripPlanId == request.PlanId, cancellationToken)
                      ?? throw new NotFoundException("resource.journey", request.PlanId);
        var today = JourneyMapping.LatestToday(clock);
        if (journey.CannotMark(request.Day, today) is { } reason) throw JourneyMapping.Invalid(nameof(request.Day), reason);
        journey.SetDay(request.Day, request.Done, today, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);
        return journey.ToDto();
    }
}

// ---- Leaderboard ----

/// <summary>Travellers ranked by completed trips, then days travelled, then countries; the earliest to get there first.</summary>
public sealed record GetLeaderboardQuery(Guid? CurrentUserId, int Top = 10) : IRequest<LeaderboardDto>;

public sealed class GetLeaderboardHandler(IApplicationDbContext db, ILeaderboardSamples samples) : IRequestHandler<GetLeaderboardQuery, LeaderboardDto>
{
    public async Task<LeaderboardDto> Handle(GetLeaderboardQuery request, CancellationToken cancellationToken)
    {
        var completed = await db.Journeys.AsNoTracking()
            .Where(j => j.CompletedAtUtc != null)
            .Join(db.Users, j => j.UserId, u => u.Id, (j, u) => new
            {
                j.UserId, u.Name, j.City, j.CountryCode, j.StartDate, j.EndDate, CompletedAtUtc = j.CompletedAtUtc!.Value,
            })
            .ToListAsync(cancellationToken);

        var real = completed.GroupBy(j => j.UserId).Select(g => new Row(
            JourneyMapping.PublicName(g.First().Name),
            g.Count(),
            g.Sum(j => j.EndDate.DayNumber - j.StartDate.DayNumber + 1),
            g.Select(j => j.CountryCode).Distinct().ToList(),
            g.OrderByDescending(j => j.CompletedAtUtc).Select(j => j.City).Distinct().ToList(),
            g.Max(j => j.CompletedAtUtc),
            false,
            g.Key == request.CurrentUserId)).ToList();

        var rows = real.Concat(samples.Get().Select(s =>
            new Row(s.Name, s.Trips, s.Days, s.Countries, s.Cities, s.LastCompletedAtUtc, true, false)));

        var ranked = Rank(rows).Take(Math.Clamp(request.Top, 1, 50)).ToList();
        // The signed-in traveller always sees their own place, even below the top.
        if (request.CurrentUserId is not null && ranked.All(e => !e.IsYou))
        {
            var me = Rank(rows).FirstOrDefault(e => e.IsYou);
            if (me is not null) ranked.Add(me);
        }
        return new LeaderboardDto(ranked, real.Count);
    }

    public static IEnumerable<LeaderboardEntryDto> Rank(IEnumerable<Row> rows) =>
        rows.OrderByDescending(r => r.Trips).ThenByDescending(r => r.Days).ThenByDescending(r => r.Countries.Count).ThenBy(r => r.Last)
            .Select((r, i) => new LeaderboardEntryDto(i + 1, r.Name, r.Trips, r.Days, r.Countries, r.Cities.Take(6).ToList(), r.Last, r.IsSample, r.IsYou));

    public sealed record Row(string Name, int Trips, int Days, IReadOnlyList<string> Countries, IReadOnlyList<string> Cities, DateTime Last, bool IsSample, bool IsYou);
}
