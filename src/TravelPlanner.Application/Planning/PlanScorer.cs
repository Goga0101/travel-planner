using TravelPlanner.Application.Common;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Planning;

public sealed record ScoreInput(
    decimal TotalCost,
    decimal BudgetAmount,
    FlightOffer Flight,
    AccommodationOffer? Accommodation,
    TimeSpan? TransferDuration,
    bool IsIncomplete);

public sealed record ScoreResult(decimal Score, IReadOnlyDictionary<string, decimal> Breakdown);

/// <summary>
/// Transparent weighted score in [0, 100]. Each criterion is normalized to [0, 1] relative to the
/// other candidates of the same search; the breakdown is stored with the plan and shown in the UI.
/// </summary>
public static class PlanScorer
{
    public static IReadOnlyList<ScoreResult> Score(IReadOnlyList<ScoreInput> inputs, RankingWeights weights)
    {
        if (inputs.Count == 0) return [];

        var minCost = inputs.Min(i => i.TotalCost);
        var maxCost = inputs.Max(i => i.TotalCost);
        var flightBurden = inputs.Select(FlightBurden).ToList();
        var known = flightBurden.Where(b => b.HasValue).Select(b => b!.Value).ToList();
        var minBurden = known.Count > 0 ? known.Min() : 0;
        var maxBurden = known.Count > 0 ? known.Max() : 0;

        var results = new List<ScoreResult>(inputs.Count);
        for (var idx = 0; idx < inputs.Count; idx++)
        {
            var input = inputs[idx];
            var cost = Normalize(input.TotalCost, minCost, maxCost, higherIsBetter: false);
            var flight = flightBurden[idx] is { } burden ? Normalize(burden, minBurden, maxBurden, higherIsBetter: false) : 0.5m;
            var accommodation = AccommodationQuality(input.Accommodation);
            var transfer = TransferConvenience(input.TransferDuration);
            var remaining = input.BudgetAmount <= 0 ? 0 : Math.Clamp((input.BudgetAmount - input.TotalCost) / input.BudgetAmount, 0m, 1m);

            var breakdown = new Dictionary<string, decimal>
            {
                ["cost"] = Round(cost * weights.Cost * 100),
                ["flight"] = Round(flight * weights.Flight * 100),
                ["accommodation"] = Round(accommodation * weights.Accommodation * 100),
                ["transfer"] = Round(transfer * weights.Transfer * 100),
                ["budgetRemaining"] = Round(remaining * weights.BudgetRemaining * 100)
            };
            var score = breakdown.Values.Sum();
            if (input.IsIncomplete)
            {
                var penalized = score * weights.IncompletePenalty;
                breakdown["incompletePenalty"] = Round(penalized - score);
                score = penalized;
            }
            results.Add(new ScoreResult(Round(score), breakdown));
        }
        return results;
    }

    /// <summary>Hours in the air plus a 2-hour equivalent per stop. Null when durations are unknown.</summary>
    private static decimal? FlightBurden(ScoreInput input)
    {
        var duration = input.Flight.TotalDuration;
        if (duration is null) return null;
        var stops = input.Flight.Outbound.Stops + input.Flight.Inbound.Stops;
        return (decimal)duration.Value.TotalHours + 2m * stops;
    }

    private static decimal AccommodationQuality(AccommodationOffer? offer)
    {
        if (offer is null) return 0m;
        decimal? stars = offer.Stars is { } s ? Math.Clamp(s / 5m, 0, 1) : null;
        decimal? review = offer.ReviewScore is { } r ? Math.Clamp(r / 10m, 0, 1) : null;
        return (stars, review) switch
        {
            ({ } a, { } b) => (a + b) / 2,
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => 0.4m
        };
    }

    /// <summary>≤ 30 min = 1, ≥ 90 min = 0, linear in between; unknown = 0.5.</summary>
    private static decimal TransferConvenience(TimeSpan? duration)
    {
        if (duration is null) return 0.5m;
        var minutes = (decimal)duration.Value.TotalMinutes;
        return Math.Clamp(1m - (minutes - 30m) / 60m, 0m, 1m);
    }

    private static decimal Normalize(decimal value, decimal min, decimal max, bool higherIsBetter)
    {
        if (max == min) return 1m;
        var n = (value - min) / (max - min);
        return higherIsBetter ? n : 1m - n;
    }

    private static decimal Round(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static bool IsIncomplete(BudgetFitStatus fit) => fit == BudgetFitStatus.WithinBudgetIncomplete;
}
