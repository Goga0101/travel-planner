using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Planning;

public sealed record CalculatedLine(PlanItemDraft Draft, decimal? ConvertedAmount, ExchangeRate? Rate, bool ConversionFailed);

public sealed record BudgetResult(
    string Currency,
    decimal BudgetAmount,
    decimal TotalEstimatedCost,
    decimal RemainingBudget,
    BudgetFitStatus Fit,
    IReadOnlyList<CalculatedLine> Lines,
    IReadOnlyList<string> MissingCategories,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, decimal> CategoryTotals)
{
    public bool IsAffordable => Fit is BudgetFitStatus.WithinBudget or BudgetFitStatus.WithinBudgetIncomplete;
}

/// <summary>
/// Pure budget calculation:
/// - only lines with IncludedInTotal contribute (informational fee lines never double-count);
/// - amounts in another currency are converted only with a supplied rate (rate and timestamp are kept per line);
///   without a rate the line is excluded and its category reported as missing;
/// - required lines with unknown cost make the result incomplete, never "within budget".
/// </summary>
public static class BudgetCalculator
{
    public static BudgetResult Calculate(
        decimal budgetAmount,
        string currency,
        IReadOnlyList<PlanItemDraft> drafts,
        IReadOnlyDictionary<string, ExchangeRate> ratesToBudgetCurrency,
        decimal contingencyPercent = 0m,
        DateTimeOffset? nowUtc = null)
    {
        currency = currency.ToUpperInvariant();
        var lines = new List<CalculatedLine>();
        var missing = new List<string>();
        var warnings = new List<string>();

        foreach (var draft in drafts.Where(d => d.Type != PlanItemType.Contingency))
        {
            if (draft.Amount is null || string.IsNullOrWhiteSpace(draft.Currency))
            {
                lines.Add(new CalculatedLine(draft, null, null, false));
                if (draft.IsRequired && draft.IncludedInTotal) AddOnce(missing, draft.Category);
                continue;
            }

            var lineCurrency = draft.Currency.ToUpperInvariant();
            if (lineCurrency == currency)
            {
                lines.Add(new CalculatedLine(draft, Round(draft.Amount.Value), null, false));
                continue;
            }

            if (ratesToBudgetCurrency.TryGetValue(lineCurrency, out var rate) && rate.To.Equals(currency, StringComparison.OrdinalIgnoreCase) && rate.Rate > 0)
            {
                lines.Add(new CalculatedLine(draft, Round(draft.Amount.Value * rate.Rate), rate, false));
                continue;
            }

            lines.Add(new CalculatedLine(draft, null, null, true));
            if (draft.IncludedInTotal)
            {
                AddOnce(missing, draft.Category);
                AddOnce(warnings, Common.T.Get("warn.noRate", lineCurrency, currency, draft.Title));
            }
        }

        var subtotal = lines.Where(l => l.Draft.IncludedInTotal && l.ConvertedAmount.HasValue).Sum(l => l.ConvertedAmount!.Value);

        if (contingencyPercent > 0 && subtotal > 0)
        {
            var contingency = Round(subtotal * contingencyPercent / 100m);
            var at = nowUtc ?? DateTimeOffset.UtcNow;
            var draft = new PlanItemDraft
            {
                Type = PlanItemType.Contingency,
                Category = CostCategories.Contingency,
                Title = Common.T.Get("estimate.contingencyTitle", contingencyPercent),
                Provider = Common.T.Get("estimate.provider"),
                Amount = contingency,
                Currency = currency,
                Verification = VerificationStatus.Estimate,
                RetrievedAtUtc = at,
                DetailsJson = Common.AppJson.Serialize(new EstimateItemDetails(Common.T.Get("estimate.contingencyBasis", contingencyPercent), null, null, null, contingencyPercent))
            };
            lines.Add(new CalculatedLine(draft, contingency, null, false));
        }

        var total = lines.Where(l => l.Draft.IncludedInTotal && l.ConvertedAmount.HasValue).Sum(l => l.ConvertedAmount!.Value);
        var remaining = budgetAmount - total;

        var categoryTotals = lines
            .Where(l => l.Draft.IncludedInTotal && l.ConvertedAmount.HasValue)
            .GroupBy(l => l.Draft.Category)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.ConvertedAmount!.Value));

        BudgetFitStatus fit;
        if (!lines.Any(l => l.Draft.IncludedInTotal && l.ConvertedAmount.HasValue)) fit = BudgetFitStatus.Unknown;
        else if (total > budgetAmount) fit = BudgetFitStatus.OverBudget;
        else if (missing.Count > 0) fit = BudgetFitStatus.WithinBudgetIncomplete;
        else fit = BudgetFitStatus.WithinBudget;

        return new BudgetResult(currency, budgetAmount, total, remaining, fit, lines, missing, warnings, categoryTotals);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static void AddOnce(List<string> list, string value)
    {
        if (!list.Contains(value)) list.Add(value);
    }
}
