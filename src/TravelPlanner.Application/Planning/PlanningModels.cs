using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Entities;
using TravelPlanner.Domain.Enums;
using TravelPlanner.Domain.ValueObjects;

namespace TravelPlanner.Application.Planning;

public sealed record PlanningRequest(
    Airport Origin,
    IReadOnlyList<Destination> Destinations,
    DateOnly EarliestDeparture,
    DateOnly LatestReturn,
    int DurationDays,
    int Travelers,
    decimal Budget,
    string Currency,
    TripPreferences Preferences);

public sealed record PlanningProgress(int Percent, string Message);

public sealed class PlanCandidate
{
    public required Destination Destination { get; init; }
    public required Airport ArrivalAirport { get; init; }
    public required TravelDates Dates { get; init; }
    public required FlightOffer Flight { get; init; }
    public AccommodationOffer? Accommodation { get; init; }
    public List<PlanItemDraft> Drafts { get; init; } = [];
    public BudgetResult Budget { get; set; } = null!;
    public ScoreResult? Score { get; set; }
    public TimeSpan? TransferDuration { get; set; }
    public List<string> Warnings { get; } = [];

    public bool IsDemo => Drafts.Any(d => d.Verification == VerificationStatus.Demo);
}

public sealed record PlanningStats(int DateCombinations, int DestinationsSearched, int FlightSearches, int AccommodationSearches, int TransportSearches);

public sealed record PlanningResult(
    IReadOnlyList<PlanCandidate> Plans,
    bool AnyWithinBudget,
    IReadOnlyList<string> Warnings,
    PlanningStats Stats);

/// <summary>Thread-safe collection of user-facing warnings; provider failures are aggregated, not repeated.</summary>
internal sealed class WarningCollector
{
    private readonly object _lock = new();
    private readonly List<string> _warnings = [];
    private readonly Dictionary<(string Area, string Provider, ProviderErrorKind Kind), int> _failures = new();

    public void Add(string warning)
    {
        lock (_lock)
        {
            if (!_warnings.Contains(warning)) _warnings.Add(warning);
        }
    }

    public void ProviderFailure(string area, string provider, ProviderErrorKind kind)
    {
        lock (_lock)
        {
            var key = (area, provider, kind);
            _failures[key] = _failures.GetValueOrDefault(key) + 1;
        }
    }

    public IReadOnlyList<string> ToList()
    {
        lock (_lock)
        {
            var failures = _failures.Select(f =>
            {
                var area = T.Get("area." + f.Key.Area);
                return f.Key.Kind switch
                {
                    ProviderErrorKind.NotConfigured => T.Get("provider.notConfigured", area, f.Key.Provider),
                    ProviderErrorKind.RateLimited => T.Get("provider.rateLimited", area, f.Key.Provider, f.Value),
                    ProviderErrorKind.Timeout => T.Get("provider.timeout", area, f.Key.Provider, f.Value),
                    _ => T.Get("provider.failed", area, f.Key.Provider, f.Value, f.Key.Kind)
                };
            });
            return _warnings.Concat(failures).ToList();
        }
    }
}
