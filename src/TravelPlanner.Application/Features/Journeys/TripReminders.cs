using System.Globalization;
using System.Net;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TravelPlanner.Application.Common;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Features.Journeys;

public sealed record EmailMessage(string ToAddress, string ToName, string Subject, string Html, string Text);

/// <summary>Sends one e-mail (Brevo in production). Not configured = reminders wait until it is.</summary>
public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class TripReminderOptions
{
    public const string SectionName = "TripReminders";

    /// <summary>Public site address the e-mail links point to (no trailing slash).</summary>
    public string SiteUrl { get; set; } = "https://goga0101.github.io/travel-planner";
    /// <summary>The reminder times below are in this zone (most travellers fly from Georgia).</summary>
    public string TimeZone { get; set; } = "Asia/Tbilisi";
    public int CheckEveryMinutes { get; set; } = 15;
}

/// <summary>
/// When the two reminder e-mails of a booked trip are due, in the configured local time:
/// "have a good flight" on the departure day at 06:00 (the evening before at 20:00 when the flight leaves before 09:00),
/// until the flight leaves; "your trip has begun" on the second day of the trip, from 08:00 until the end of that day.
/// A trip booked too late for a reminder simply skips it.
/// </summary>
public static class TripReminderSchedule
{
    public static bool FlightEmailDue(Journey j, DateTime nowLocal)
    {
        if (j.Status != JourneyStatus.Booked || j.FlightEmailSentAtUtc is not null) return false;
        var day = j.StartDate.ToDateTime(TimeOnly.MinValue);
        var sendAt = j.DepartureLocal is { } dep && dep.TimeOfDay < TimeSpan.FromHours(9) ? day.AddHours(-4) : day.AddHours(6);
        var deadline = j.DepartureLocal ?? day.AddDays(1);
        return nowLocal >= sendAt && nowLocal < deadline;
    }

    public static bool StartEmailDue(Journey j, DateTime nowLocal)
    {
        if (j.Status != JourneyStatus.Booked || j.StartEmailSentAtUtc is not null || j.DayCount < 2) return false;
        var secondDay = j.StartDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        return nowLocal >= secondDay.AddHours(8) && nowLocal < secondDay.AddDays(1);
    }
}

/// <summary>The reminder e-mails, in Georgian or English. Every inserted value is HTML-encoded.</summary>
public static class TripEmails
{
    public static EmailMessage FlightWishes(User user, Journey j, string siteUrl)
    {
        var ka = j.Lang == "ka";
        var first = FirstName(user.Name);
        var when = j.DepartureLocal is { } d
            ? (ka ? $"გაფრენა: {d.ToString("d MMMM, HH:mm", Georgian)}" : $"Departure: {d.ToString("d MMMM, HH:mm", CultureInfo.InvariantCulture)}")
            : null;
        var subject = ka ? $"მშვიდობიან ფრენას გისურვებთ, {first}! ✈️" : $"Have a good flight, {first}! ✈️";
        string[] lines = ka
            ? [$"გამარჯობა, {first}!", $"დღეს იწყება შენი მოგზაურობა. მიმართულება: {j.City}.", .. when is null ? Array.Empty<string>() : [when],
               "გისურვებთ მშვიდობიან ფრენას და დაუვიწყარ დღეებს.", "მადლობა, რომ მოგზაურობის დასაგეგმად Tripora აირჩიე!"]
            : [$"Hello {first},", $"Your trip starts today. Destination: {j.City}.", .. when is null ? Array.Empty<string>() : [when],
               "We wish you a safe flight and unforgettable days.", "Thank you for planning your trip with Tripora!"];
        return Build(user, subject, lines, [(ka ? "მოგზაურობის გეგმა" : "Your trip plan", PlanUrl(siteUrl, j))]);
    }

    public static EmailMessage TripStarted(User user, Journey j, string siteUrl)
    {
        var ka = j.Lang == "ka";
        var first = FirstName(user.Name);
        var subject = ka ? $"{first}, შენი მოგზაურობა დაიწყო! 🧳" : $"{first}, your trip has begun! 🧳";
        string[] lines = ka
            ? [$"გამარჯობა, {first}!", $"შენი მოგზაურობა ({j.City}) უკვე დაიწყო. იმედია, ყველაფერი კარგად მიდის!",
               "ყოველ საღამოს მოგზაურობის დღიურში აღნიშნე გავლილი დღე. როცა ყველა დღეს აღნიშნავ, მოხვდები Tripora-ს მოგზაურთა ლიდერბორდზე.",
               "გაგვიზიარე შთაბეჭდილებები და ემოციები: უბრალოდ უპასუხე ამ წერილს."]
            : [$"Hello {first},", $"Your trip to {j.City} has begun. We hope everything is going well!",
               "Each evening, tick off the day in your trip diary. Once every day is ticked, you join the Tripora travellers' leaderboard.",
               "Share your impressions with us: just reply to this e-mail."];
        return Build(user, subject, lines,
            [(ka ? "დღეების აღნიშვნა" : "Tick off your days", PlanUrl(siteUrl, j)),
             (ka ? "ლიდერბორდი" : "Leaderboard", $"{siteUrl}/{Lang(j)}#leaderboard")]);
    }

    private static readonly CultureInfo Georgian = CultureInfo.GetCultureInfo("ka-GE");

    private static string Lang(Journey j) => j.Lang == "en" ? "en" : "ka";
    private static string PlanUrl(string siteUrl, Journey j) => $"{siteUrl}/{Lang(j)}/plans/{j.TripPlanId}";
    private static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    private static EmailMessage Build(User user, string subject, IReadOnlyList<string> lines, IReadOnlyList<(string Label, string Url)> links)
    {
        static string E(string s) => WebUtility.HtmlEncode(s);
        var html =
            "<div style=\"font-family:Arial,Helvetica,sans-serif;max-width:560px;margin:0 auto;padding:24px;color:#0f172a;line-height:1.55\">" +
            "<p style=\"margin:0 0 18px;font-size:20px;font-weight:700;color:#0e7c66\">Tripora</p>" +
            string.Concat(lines.Select(l => $"<p style=\"margin:0 0 12px;font-size:15px\">{E(l)}</p>")) +
            "<p style=\"margin:22px 0 0\">" +
            string.Concat(links.Select((l, i) =>
                $"<a href=\"{E(l.Url)}\" style=\"display:inline-block;margin:0 8px 8px 0;padding:10px 18px;border-radius:999px;text-decoration:none;font-weight:700;" +
                (i == 0 ? "background:#0e7c66;color:#ffffff" : "border:1px solid #0e7c66;color:#0e7c66") + $"\">{E(l.Label)}</a>")) +
            "</p></div>";
        var text = string.Join("\n\n", lines) + "\n\n" + string.Join("\n", links.Select(l => $"{l.Label}: {l.Url}"));
        return new EmailMessage(user.Email!, user.Name, subject, html, text);
    }
}

/// <summary>Sends the reminder e-mails that are due now; each one at most once per trip.</summary>
public sealed record SendTripRemindersCommand : IRequest<int>;

public sealed class SendTripRemindersHandler(
    IApplicationDbContext db, IEmailSender email, TimeProvider clock, IOptions<TripReminderOptions> options, ILogger<SendTripRemindersHandler> logger)
    : IRequestHandler<SendTripRemindersCommand, int>
{
    public async Task<int> Handle(SendTripRemindersCommand request, CancellationToken cancellationToken)
    {
        if (!email.IsConfigured) return 0;
        var o = options.Value;
        var nowUtc = clock.GetUtcNow().UtcDateTime;
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ZoneOrUtc(o.TimeZone));
        var today = DateOnly.FromDateTime(nowLocal);
        // Departure tomorrow (evening-before e-mail) back to the second day of the trip.
        var from = today.AddDays(-1);
        var to = today.AddDays(1);

        var candidates = await db.Journeys
            .Where(j => j.Status == JourneyStatus.Booked && j.StartDate >= from && j.StartDate <= to
                        && (j.FlightEmailSentAtUtc == null || j.StartEmailSentAtUtc == null))
            .Join(db.Users.Where(u => u.Email != null), j => j.UserId, u => u.Id, (j, u) => new { Journey = j, User = u })
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var c in candidates)
        {
            var site = o.SiteUrl.TrimEnd('/');
            try
            {
                if (TripReminderSchedule.FlightEmailDue(c.Journey, nowLocal))
                {
                    await email.SendAsync(TripEmails.FlightWishes(c.User, c.Journey, site), cancellationToken);
                    c.Journey.MarkFlightEmailSent(nowUtc);
                    sent++;
                }
                if (TripReminderSchedule.StartEmailDue(c.Journey, nowLocal))
                {
                    await email.SendAsync(TripEmails.TripStarted(c.User, c.Journey, site), cancellationToken);
                    c.Journey.MarkStartEmailSent(nowUtc);
                    sent++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Try again on the next round.
                logger.LogWarning("Trip reminder for journey {JourneyId} failed ({Error})", c.Journey.Id, ex.GetType().Name);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        return sent;
    }

    private static TimeZoneInfo ZoneOrUtc(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}
