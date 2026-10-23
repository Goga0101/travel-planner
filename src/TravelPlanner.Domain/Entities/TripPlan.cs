using TravelPlanner.Domain.Common;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Domain.Entities;

public class TripPlan : Entity
{
    private TripPlan() { }

    public TripPlan(Guid tripSearchId, Guid destinationId, DateOnly departureDate, DateOnly returnDate, int travelers,
        string currency, decimal budgetAmount, bool isDemo, DateTimeOffset nowUtc)
    {
        TripSearchId = tripSearchId;
        DestinationId = destinationId;
        DepartureDate = departureDate;
        ReturnDate = returnDate;
        Travelers = travelers;
        Currency = currency;
        BudgetAmount = budgetAmount;
        IsDemo = isDemo;
        CreatedAtUtc = nowUtc;
    }

    public Guid TripSearchId { get; private set; }
    public TripSearch? TripSearch { get; private set; }
    public Guid DestinationId { get; private set; }
    public Destination? Destination { get; private set; }

    public int Rank { get; set; }
    public decimal Score { get; set; }
    /// <summary>JSON with the per-criterion score contributions (transparent ranking).</summary>
    public string ScoreBreakdownJson { get; set; } = "{}";

    public DateOnly DepartureDate { get; private set; }
    public DateOnly ReturnDate { get; private set; }
    public int Nights => ReturnDate.DayNumber - DepartureDate.DayNumber;
    public int Travelers { get; private set; }

    public string Currency { get; private set; } = null!;
    public decimal BudgetAmount { get; private set; }
    public decimal TotalEstimatedCost { get; set; }
    public decimal RemainingBudget { get; set; }
    public BudgetFitStatus BudgetFit { get; set; }
    public List<string> MissingCostCategories { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public bool IsDemo { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? PricesRefreshedAtUtc { get; set; }

    public List<TripPlanItem> Items { get; private set; } = [];
}
