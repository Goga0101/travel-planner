using System.Text.Json;
using TravelPlanner.Application.Common;
using TravelPlanner.Domain.Entities;

namespace TravelPlanner.Application.Planning;

public static class PlanEntityFactory
{
    public static TripPlan Create(Guid tripSearchId, PlanCandidate candidate, int rank, int travelers, string currency,
        decimal budget, DateTimeOffset nowUtc, IOutboundLinkPolicy linkPolicy)
    {
        var plan = new TripPlan(tripSearchId, candidate.Destination.Id, candidate.Dates.Departure, candidate.Dates.Return,
            travelers, currency, budget, candidate.IsDemo, nowUtc)
        {
            Rank = rank,
            Score = candidate.Score?.Score ?? 0,
            ScoreBreakdownJson = JsonSerializer.Serialize(candidate.Score?.Breakdown ?? new Dictionary<string, decimal>(), AppJson.Options)
        };
        ApplyBudget(plan, candidate.Budget, linkPolicy);
        plan.Warnings.AddRange(candidate.Warnings.Where(w => !plan.Warnings.Contains(w)));
        return plan;
    }

    /// <summary>Replaces the plan's items and totals with the given budget result.</summary>
    public static void ApplyBudget(TripPlan plan, BudgetResult budget, IOutboundLinkPolicy linkPolicy)
    {
        plan.TotalEstimatedCost = budget.TotalEstimatedCost;
        plan.RemainingBudget = budget.RemainingBudget;
        plan.BudgetFit = budget.Fit;
        plan.MissingCostCategories = budget.MissingCategories.ToList();
        plan.Warnings = budget.Warnings.ToList();

        plan.Items.Clear();
        var order = 0;
        foreach (var line in budget.Lines)
        {
            var d = line.Draft;
            plan.Items.Add(new TripPlanItem
            {
                TripPlanId = plan.Id,
                Type = d.Type,
                Direction = d.Direction,
                SortOrder = order++,
                Category = d.Category,
                IsRequired = d.IsRequired,
                Title = Truncate(d.Title, 300)!,
                Summary = Truncate(d.Summary, 1000),
                ProviderName = Truncate(d.Provider, 200)!,
                ProviderOfferId = Truncate(d.ProviderOfferId, 500),
                Price = d.Amount,
                Currency = d.Currency?.ToUpperInvariant(),
                ConvertedPrice = line.ConvertedAmount,
                ExchangeRate = line.Rate?.Rate,
                ExchangeRateSource = line.Rate?.Source,
                ExchangeRateAsOfUtc = line.Rate?.AsOfUtc,
                IncludedInTotal = d.IncludedInTotal,
                BookingUrl = linkPolicy.Sanitize(d.BookingUrl),
                RetrievedAtUtc = d.RetrievedAtUtc,
                ExpiresAtUtc = d.ExpiresAtUtc,
                Verification = d.Verification,
                DetailsJson = d.DetailsJson
            });
        }
    }

    public static PlanItemDraft ToDraft(TripPlanItem item) => new()
    {
        Type = item.Type,
        Direction = item.Direction,
        Category = item.Category,
        Title = item.Title,
        Summary = item.Summary,
        Provider = item.ProviderName,
        ProviderOfferId = item.ProviderOfferId,
        Amount = item.Price,
        Currency = item.Currency,
        IncludedInTotal = item.IncludedInTotal,
        IsRequired = item.IsRequired,
        BookingUrl = item.BookingUrl,
        RetrievedAtUtc = item.RetrievedAtUtc,
        ExpiresAtUtc = item.ExpiresAtUtc,
        Verification = item.Verification,
        DetailsJson = item.DetailsJson
    };

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
