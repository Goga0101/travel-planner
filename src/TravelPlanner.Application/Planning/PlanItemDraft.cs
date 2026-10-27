using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Planning;

public static class CostCategories
{
    public const string Flights = "Flights";
    public const string Accommodation = "Accommodation";
    public const string AirportTransfers = "Airport transfers";
    public const string TaxesAndFees = "Taxes & fees";
    public const string Food = "Food (estimate)";
    public const string LocalTransport = "Local transport (estimate)";
    public const string Contingency = "Contingency";

    public static string For(PlanItemType type) => type switch
    {
        PlanItemType.Flight => Flights,
        PlanItemType.Accommodation => Accommodation,
        PlanItemType.Transfer => AirportTransfers,
        PlanItemType.Fee => TaxesAndFees,
        PlanItemType.Contingency => Contingency,
        _ => "Other"
    };
}

/// <summary>An unsaved plan line used for budget calculation and later persisted as a TripPlanItem.</summary>
public sealed record PlanItemDraft
{
    public required PlanItemType Type { get; init; }
    public TransferDirection Direction { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public required string Provider { get; init; }
    public string? ProviderOfferId { get; init; }
    /// <summary>Null means the cost is unknown.</summary>
    public decimal? Amount { get; init; }
    public string? Currency { get; init; }
    /// <summary>False for informational lines (e.g. fees already included in the accommodation price).</summary>
    public bool IncludedInTotal { get; init; } = true;
    /// <summary>A required category whose cost is unknown makes the plan "incomplete".</summary>
    public bool IsRequired { get; init; }
    public string? BookingUrl { get; init; }
    public required DateTimeOffset RetrievedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required VerificationStatus Verification { get; init; }
    public string DetailsJson { get; init; } = "{}";
}

// ---- Normalized detail payloads stored in TripPlanItem.DetailsJson ----

public sealed record FlightItemDetails(
    FlightItinerary Outbound,
    FlightItinerary Inbound,
    int Passengers,
    int? IncludedCheckedBags,
    string? BaggageNote,
    bool PriceExtrapolatedFromSinglePassenger,
    string? ValidatingCarrier,
    string? SourceNote,
    IReadOnlyList<FlightTicket>? Tickets = null);

public sealed record AccommodationItemDetails(
    string Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    decimal? Stars,
    decimal? ReviewScore,
    string? Category,
    DateOnly CheckIn,
    DateOnly CheckOut,
    int Nights,
    int Guests,
    int Rooms,
    string? CancellationPolicy,
    DateTimeOffset? FreeCancellationUntil,
    string? MealPlan,
    IReadOnlyList<MandatoryFee> Fees);

public sealed record FeeItemDetails(string Name, bool IncludedInPrice, bool PayableAtProperty, string RelatedTo);

public sealed record TransferItemDetails(
    string AirportIata,
    string AirportName,
    double AirportLatitude,
    double AirportLongitude,
    double? AccommodationLatitude,
    double? AccommodationLongitude,
    string? SelectedOptionTitle,
    string? MapsDirectionsUrl,
    IReadOnlyList<TransferOption> Options,
    string Disclaimer);

public sealed record EstimateItemDetails(string Basis, decimal? UnitAmount, int? Days, int? Travelers, decimal? Percent);
