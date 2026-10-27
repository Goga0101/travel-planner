using TravelPlanner.Domain.Common;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Domain.Entities;

/// <summary>A submitted trip search; doubles as the background job record.</summary>
public class TripSearch : Entity
{
    private TripSearch() { }

    public TripSearch(
        string originAirportIata, DateOnly earliestDeparture, DateOnly latestReturn, int durationDays, int travelers,
        decimal budgetAmount, string budgetCurrency, DestinationMode destinationMode, TripPreferences preferences,
        DataMode dataMode, DateTimeOffset nowUtc)
    {
        OriginAirportIata = originAirportIata.ToUpperInvariant();
        EarliestDeparture = earliestDeparture;
        LatestReturn = latestReturn;
        DurationDays = durationDays;
        Travelers = travelers;
        BudgetAmount = budgetAmount;
        BudgetCurrency = budgetCurrency.ToUpperInvariant();
        DestinationMode = destinationMode;
        Preferences = preferences;
        DataMode = dataMode;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
        Status = TripSearchStatus.Queued;
        ProgressMessage = "Queued";
    }

    public string OriginAirportIata { get; private set; } = null!;
    public DateOnly EarliestDeparture { get; private set; }
    public DateOnly LatestReturn { get; private set; }
    public int DurationDays { get; private set; }
    public int Travelers { get; private set; }
    public decimal BudgetAmount { get; private set; }
    public string BudgetCurrency { get; private set; } = null!;
    public DestinationMode DestinationMode { get; private set; }
    public TripPreferences Preferences { get; private set; } = new();
    public DataMode DataMode { get; private set; }
    /// <summary>Two-letter language code used for server-generated texts of this search ("en" or "ka").</summary>
    public string Language { get; private set; } = "en";

    public void SetLanguage(string language) => Language = language;

    public TripSearchStatus Status { get; private set; }
    public int ProgressPercent { get; private set; }
    public string ProgressMessage { get; private set; } = null!;
    public string? ErrorMessage { get; private set; }
    public List<string> Warnings { get; private set; } = [];
    /// <summary>Automatic mode: only destinations on these continents (empty = all).</summary>
    public List<string> Continents { get; private set; } = [];
    /// <summary>Every flight offer the search found (JSON list), shown in the flights list.</summary>
    public string FoundFlightsJson { get; private set; } = "[]";

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public List<TripSearchDestination> SelectedDestinations { get; private set; } = [];
    public List<TripPlan> Plans { get; private set; } = [];

    public int Nights => DurationDays - 1;
    public bool IsFinished => Status is TripSearchStatus.Completed or TripSearchStatus.CompletedNoMatch
        or TripSearchStatus.Failed or TripSearchStatus.Cancelled;

    public void SelectDestination(Guid destinationId)
    {
        if (SelectedDestinations.All(d => d.DestinationId != destinationId))
            SelectedDestinations.Add(new TripSearchDestination { TripSearchId = Id, DestinationId = destinationId });
    }

    public void RestrictToContinents(IEnumerable<string> continents) =>
        Continents = continents.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public void SetFoundFlights(string json) => FoundFlightsJson = json;

    public void MarkRunning(DateTimeOffset nowUtc)
    {
        Status = TripSearchStatus.Running;
        StartedAtUtc = nowUtc;
        ReportProgress(1, "Search started", nowUtc);
    }

    public void ReportProgress(int percent, string message, DateTimeOffset nowUtc)
    {
        ProgressPercent = Math.Clamp(percent, 0, 100);
        ProgressMessage = message;
        UpdatedAtUtc = nowUtc;
    }

    public void AddWarning(string warning)
    {
        if (!Warnings.Contains(warning)) Warnings.Add(warning);
    }

    public void Complete(bool hasPlansWithinBudget, DateTimeOffset nowUtc)
    {
        Status = hasPlansWithinBudget ? TripSearchStatus.Completed : TripSearchStatus.CompletedNoMatch;
        CompletedAtUtc = nowUtc;
        ReportProgress(100, hasPlansWithinBudget ? "Search completed" : "No plans fit the budget", nowUtc);
    }

    public void Fail(string safeMessage, DateTimeOffset nowUtc)
    {
        Status = TripSearchStatus.Failed;
        ErrorMessage = safeMessage;
        CompletedAtUtc = nowUtc;
        ReportProgress(ProgressPercent, "Search failed", nowUtc);
    }

    public void Cancel(DateTimeOffset nowUtc)
    {
        if (IsFinished) return;
        Status = TripSearchStatus.Cancelled;
        CompletedAtUtc = nowUtc;
        ReportProgress(ProgressPercent, "Search cancelled", nowUtc);
    }
}

public class TripSearchDestination
{
    public Guid TripSearchId { get; set; }
    public Guid DestinationId { get; set; }
    public Destination? Destination { get; set; }
}
