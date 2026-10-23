using TravelPlanner.Application.Planning;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;
using static TravelPlanner.UnitTests.TestData;

namespace TravelPlanner.UnitTests;

public class BudgetCalculatorTests
{
    private static readonly Dictionary<string, ExchangeRate> NoRates = new();

    [Fact]
    public void Sums_Flights_Accommodation_And_Transfers_In_Budget_Currency()
    {
        var result = BudgetCalculator.Calculate(1000m, "EUR",
        [
            Line(PlanItemType.Flight, 350.10m),
            Line(PlanItemType.Accommodation, 400m),
            Line(PlanItemType.Transfer, 12.45m),
            Line(PlanItemType.Transfer, 12.45m)
        ], NoRates);

        Assert.Equal(775.00m, result.TotalEstimatedCost);
        Assert.Equal(225.00m, result.RemainingBudget);
        Assert.Equal(BudgetFitStatus.WithinBudget, result.Fit);
        Assert.Empty(result.MissingCategories);
        Assert.Equal(24.90m, result.CategoryTotals[Application.Planning.CostCategories.AirportTransfers]);
    }

    [Fact]
    public void Over_Budget_Is_Never_Marked_Affordable()
    {
        var result = BudgetCalculator.Calculate(500m, "EUR", [Line(PlanItemType.Flight, 300m), Line(PlanItemType.Accommodation, 200.01m)], NoRates);

        Assert.Equal(BudgetFitStatus.OverBudget, result.Fit);
        Assert.False(result.IsAffordable);
        Assert.Equal(-0.01m, result.RemainingBudget);
    }

    [Fact]
    public void Exactly_On_Budget_Is_Within_Budget()
    {
        var result = BudgetCalculator.Calculate(500m, "EUR", [Line(PlanItemType.Flight, 300m), Line(PlanItemType.Accommodation, 200m)], NoRates);

        Assert.Equal(BudgetFitStatus.WithinBudget, result.Fit);
        Assert.Equal(0m, result.RemainingBudget);
    }

    [Fact]
    public void Different_Currency_Without_Rate_Is_Excluded_And_Reported()
    {
        var result = BudgetCalculator.Calculate(1000m, "EUR",
            [Line(PlanItemType.Flight, 300m), Line(PlanItemType.Accommodation, 500m, "USD")], NoRates);

        Assert.Equal(300m, result.TotalEstimatedCost);
        Assert.Equal(BudgetFitStatus.WithinBudgetIncomplete, result.Fit);
        Assert.Contains(Application.Planning.CostCategories.Accommodation, result.MissingCategories);
        Assert.Contains(result.Warnings, w => w.Contains("USD->EUR"));
        Assert.True(result.Lines.Single(l => l.Draft.Type == PlanItemType.Accommodation).ConversionFailed);
    }

    [Fact]
    public void Different_Currency_With_Rate_Is_Converted_And_Rate_Recorded()
    {
        var rate = new ExchangeRate("USD", "EUR", 0.9m, "ECB", Now.AddHours(-3));
        var result = BudgetCalculator.Calculate(1000m, "EUR",
            [Line(PlanItemType.Flight, 300m), Line(PlanItemType.Accommodation, 500m, "USD")],
            new Dictionary<string, ExchangeRate> { ["USD"] = rate });

        var line = result.Lines.Single(l => l.Draft.Type == PlanItemType.Accommodation);
        Assert.Equal(450m, line.ConvertedAmount);
        Assert.Same(rate, line.Rate);
        Assert.Equal(750m, result.TotalEstimatedCost);
        Assert.Equal(BudgetFitStatus.WithinBudget, result.Fit);
    }

    [Fact]
    public void Rate_For_Wrong_Target_Currency_Is_Not_Used()
    {
        var wrong = new ExchangeRate("USD", "GBP", 0.8m, "ECB", Now);
        var result = BudgetCalculator.Calculate(1000m, "EUR", [Line(PlanItemType.Flight, 100m, "USD")],
            new Dictionary<string, ExchangeRate> { ["USD"] = wrong });

        Assert.Equal(BudgetFitStatus.Unknown, result.Fit);
        Assert.Equal(0m, result.TotalEstimatedCost);
    }

    [Fact]
    public void Unknown_Required_Cost_Makes_Plan_Incomplete()
    {
        var result = BudgetCalculator.Calculate(1000m, "EUR",
            [Line(PlanItemType.Flight, 300m), Line(PlanItemType.Accommodation, 400m), Line(PlanItemType.Transfer, null, null)], NoRates);

        Assert.Equal(BudgetFitStatus.WithinBudgetIncomplete, result.Fit);
        Assert.Equal([Application.Planning.CostCategories.AirportTransfers], result.MissingCategories);
    }

    [Fact]
    public void Unknown_Optional_Cost_Does_Not_Make_Plan_Incomplete()
    {
        var result = BudgetCalculator.Calculate(1000m, "EUR",
            [Line(PlanItemType.Flight, 300m), Line(PlanItemType.DailyExpenseEstimate, null, null, required: false, category: "Food")], NoRates);

        Assert.Equal(BudgetFitStatus.WithinBudget, result.Fit);
    }

    [Fact]
    public void No_Known_Costs_Is_Unknown_Not_Within_Budget()
    {
        var result = BudgetCalculator.Calculate(1000m, "EUR", [Line(PlanItemType.Flight, null, null)], NoRates);

        Assert.Equal(BudgetFitStatus.Unknown, result.Fit);
        Assert.False(result.IsAffordable);
    }

    [Fact]
    public void Fees_Included_In_Price_Are_Not_Double_Counted()
    {
        var offer = Stay("h1", 400m, fees:
        [
            new MandatoryFee("VAT", 36m, "EUR", IncludedInPrice: true, PayableAtProperty: false),
            new MandatoryFee("City tax", 10m, "EUR", IncludedInPrice: false, PayableAtProperty: true)
        ]);
        var drafts = PlanItemDraftFactory.Accommodation(offer, EngineFactory.Dates(20), 1, 1);

        var result = BudgetCalculator.Calculate(1000m, "EUR", drafts.ToList(), NoRates);

        Assert.Equal(410m, result.TotalEstimatedCost); // 400 + city tax; VAT already inside the 400
        var vat = result.Lines.Single(l => l.Draft.Title.StartsWith("VAT"));
        Assert.False(vat.Draft.IncludedInTotal);
        Assert.Equal(36m, vat.ConvertedAmount); // still shown for transparency
        Assert.Equal(10m, result.CategoryTotals[Application.Planning.CostCategories.TaxesAndFees]);
    }

    [Fact]
    public void Contingency_Is_Percentage_Of_Known_Costs()
    {
        var result = BudgetCalculator.Calculate(1000m, "EUR",
            [Line(PlanItemType.Flight, 300m), Line(PlanItemType.Accommodation, 200m)], NoRates, contingencyPercent: 10m, nowUtc: Now);

        Assert.Equal(550m, result.TotalEstimatedCost);
        var contingency = result.Lines.Single(l => l.Draft.Type == PlanItemType.Contingency);
        Assert.Equal(50m, contingency.ConvertedAmount);
        Assert.Equal(VerificationStatus.Estimate, contingency.Draft.Verification);
    }

    [Fact]
    public void Existing_Contingency_Lines_Are_Recalculated_Not_Added_Twice()
    {
        var drafts = new List<PlanItemDraft> { Line(PlanItemType.Flight, 100m), Line(PlanItemType.Contingency, 999m) };

        var result = BudgetCalculator.Calculate(1000m, "EUR", drafts, NoRates, contingencyPercent: 10m, nowUtc: Now);

        Assert.Equal(110m, result.TotalEstimatedCost);
        Assert.Single(result.Lines, l => l.Draft.Type == PlanItemType.Contingency);
    }

    [Fact]
    public void Amounts_Are_Rounded_To_Cents()
    {
        var rate = new ExchangeRate("GBP", "EUR", 1.1712345m, "ECB", Now);
        var result = BudgetCalculator.Calculate(1000m, "EUR", [Line(PlanItemType.Flight, 99.99m, "GBP")],
            new Dictionary<string, ExchangeRate> { ["GBP"] = rate });

        Assert.Equal(117.11m, result.TotalEstimatedCost);
    }
}
