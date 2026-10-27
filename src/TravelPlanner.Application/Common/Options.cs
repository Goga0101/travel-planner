namespace TravelPlanner.Application.Common;

/// <summary>Request budgets and limits for the planning algorithm. See README "Search algorithm and trade-offs".</summary>
public sealed class PlanningOptions
{
    public const string SectionName = "Planning";

    /// <summary>Maximum departure dates evaluated (evenly sampled across the window when exceeded).</summary>
    public int MaxDateCombinations { get; set; } = 7;
    /// <summary>Maximum destinations considered in automatic mode.</summary>
    public int MaxAutoDestinations { get; set; } = 12;
    /// <summary>Hard cap on flight provider calls per search (all providers, destinations and dates).</summary>
    public int MaxFlightSearches { get; set; } = 40;
    /// <summary>Best date combinations per destination that proceed to accommodation search.</summary>
    public int MaxCombosPerDestinationForAccommodation { get; set; } = 2;
    public int MaxFlightOffersPerSearch { get; set; } = 10;
    /// <summary>Offers kept per destination from a whole-window search (all departure days together).</summary>
    public int MaxFlightOffersPerWindow { get; set; } = 60;
    /// <summary>Offers kept for the flights list of a search (all destinations).</summary>
    public int MaxFoundFlights { get; set; } = 300;
    public int MaxFlightsPerCandidate { get; set; } = 1;
    public int MaxAccommodationsPerCandidate { get; set; } = 3;
    public int MaxAccommodationResults { get; set; } = 20;
    public int MaxConcurrentProviderCalls { get; set; } = 4;
    public int ProviderCallTimeoutSeconds { get; set; } = 25;
    public int MaxResults { get; set; } = 5;
    public int MaxPlansPerDestination { get; set; } = 2;
    public int MaxOverBudgetFallbackPlans { get; set; } = 3;

    public RankingWeights Ranking { get; set; } = new();
}

public sealed class RankingWeights
{
    public decimal Cost { get; set; } = 0.40m;
    public decimal Flight { get; set; } = 0.25m;
    public decimal Accommodation { get; set; } = 0.15m;
    public decimal Transfer { get; set; } = 0.10m;
    public decimal BudgetRemaining { get; set; } = 0.10m;
    /// <summary>Score multiplier for plans that are missing a required cost category.</summary>
    public decimal IncompletePenalty { get; set; } = 0.85m;
}

/// <summary>Optional, operator-configured estimates. All null/0 = disabled (nothing is invented).</summary>
public sealed class BudgetOptions
{
    public const string SectionName = "Budget";

    public string? EstimatesCurrency { get; set; }
    public decimal? DailyFoodPerPerson { get; set; }
    public decimal? DailyLocalTransportPerPerson { get; set; }
    public decimal ContingencyPercent { get; set; }

    public static readonly string[] SupportedCurrencies =
        ["EUR", "USD", "GBP", "CHF", "PLN", "CZK", "HUF", "SEK", "NOK", "DKK", "RON", "TRY"];
}

public sealed class OutboundLinkOptions
{
    public const string SectionName = "OutboundLinks";

    /// <summary>Hosts (and their subdomains) that may be shown to users as booking/info links.</summary>
    public List<string> AllowedHosts { get; set; } = [];
}
