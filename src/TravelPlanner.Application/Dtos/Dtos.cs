using System.Text.Json;
using TravelPlanner.Application.Providers;
using TravelPlanner.Domain.Enums;

namespace TravelPlanner.Application.Dtos;

public sealed record AirportDto(Guid Id, string IataCode, string Name, string City, string CountryCode, double Latitude, double Longitude,
    bool IsDepartureOption, string? OfficialWebsiteUrl, string? CityCode = null, string? Country = null, string? Continent = null);

/// <summary>A city anywhere in the world that can be picked as a destination; DestinationId is set for catalog cities.</summary>
public sealed record PlaceDto(string CityCode, string City, string Country, string CountryCode, string Continent,
    IReadOnlyList<string> AirportCodes, Guid? DestinationId);

public sealed record DestinationSummaryDto(Guid Id, string Code, string City, string Country, string CountryCode, string Region,
    IReadOnlyList<string> AirportCodes, int Popularity, string? Description = null);

public sealed record TransportLinkDto(Guid Id, TransportLinkKind Kind, string Name, string? Url, string? Phone, string? Notes,
    bool IsVerified, DateOnly? VerifiedOn);

public sealed record DestinationDetailDto(Guid Id, string Code, string City, string Country, string CountryCode, string Region,
    string? Description, double Latitude, double Longitude, bool IsActive, int Popularity, bool HasTaxiTariff,
    IReadOnlyList<AirportDto> Airports, IReadOnlyList<TransportLinkDto> TransportLinks);

public sealed record TripPreferencesDto(
    bool IncludeCheckedBaggage,
    int? MinAccommodationStars,
    int? MaxFlightDurationHours,
    int? MaxStops,
    DepartureTimePreference PreferredDepartureTime,
    bool IncludeDailyExpenseEstimates,
    bool AllowSelfTransfer = true);

public sealed record DestinationRefDto(Guid Id, string Code, string City, string Country, string CountryCode);

public sealed record TripSearchCriteriaDto(
    string OriginAirportIata,
    DateOnly EarliestDeparture,
    DateOnly LatestReturn,
    int DurationDays,
    int Nights,
    int Travelers,
    decimal BudgetAmount,
    string BudgetCurrency,
    DestinationMode DestinationMode,
    IReadOnlyList<DestinationRefDto> Destinations,
    TripPreferencesDto Preferences,
    IReadOnlyList<string> Continents);

public sealed record TripSearchDto(
    Guid Id,
    TripSearchStatus Status,
    bool IsFinished,
    int ProgressPercent,
    string ProgressMessage,
    string? ErrorMessage,
    IReadOnlyList<string> Warnings,
    DataMode DataMode,
    TripSearchCriteriaDto Criteria,
    int PlanCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record CreateTripSearchResponse(Guid Id, TripSearchStatus Status, DataMode DataMode);

/// <summary>A connection airport with its city in the request language (the code itself when unknown).</summary>
public sealed record ViaStopDto(string Iata, string City, string? CountryCode);

public sealed record FlightLegSummaryDto(string From, string To, DateTime DepartureLocal, DateTime? ArrivalLocal, int? DurationMinutes, int Stops,
    IReadOnlyList<string> Carriers, string? FromCity, string? ToCity, IReadOnlyList<ViaStopDto> Via);

/// <summary>One flight found by a search (all destinations and dates, not only the ones in plans).</summary>
public sealed record FoundFlightDto(
    DestinationRefDto Destination,
    DateOnly DepartureDate,
    DateOnly ReturnDate,
    decimal Price,
    string Currency,
    decimal OriginalPrice,
    string OriginalCurrency,
    bool WithinBudget,
    string Provider,
    VerificationStatus Verification,
    FlightLegSummaryDto Outbound,
    FlightLegSummaryDto Inbound,
    string? BookingUrl,
    DateTimeOffset RetrievedAtUtc,
    bool SelfTransfer,
    IReadOnlyList<FlightTicket> Tickets);

public sealed record FlightSummaryDto(string Provider, decimal? Price, string? Currency, VerificationStatus Verification,
    FlightLegSummaryDto? Outbound, FlightLegSummaryDto? Inbound, int? IncludedCheckedBags, bool HasBookingLink, bool SelfTransfer);

public sealed record AccommodationSummaryDto(string Name, string Provider, decimal? Price, string? Currency, decimal? Stars,
    decimal? ReviewScore, string? Address, VerificationStatus Verification, bool HasBookingLink, string? CancellationPolicy);

public sealed record TransferSummaryDto(string? SelectedOption, int? DurationMinutes, decimal? EstimatedCost, string? Currency, VerificationStatus Verification);

public sealed record SourceVerificationDto(PlanItemType Type, TransferDirection Direction, string Provider, VerificationStatus Verification,
    DateTimeOffset RetrievedAtUtc, DateTimeOffset? ExpiresAtUtc, bool IsExpired);

public sealed record TripPlanSummaryDto(
    Guid Id,
    Guid TripSearchId,
    int Rank,
    decimal Score,
    IReadOnlyDictionary<string, decimal> ScoreBreakdown,
    DestinationRefDto Destination,
    DateOnly DepartureDate,
    DateOnly ReturnDate,
    int Nights,
    int Travelers,
    string Currency,
    decimal BudgetAmount,
    decimal TotalEstimatedCost,
    decimal RemainingBudget,
    BudgetFitStatus BudgetFit,
    IReadOnlyList<string> MissingCostCategories,
    IReadOnlyList<string> Warnings,
    bool IsDemo,
    bool HasExpiredItems,
    DateTimeOffset? EarliestExpiryUtc,
    DateTimeOffset? PricesRefreshedAtUtc,
    FlightSummaryDto? Flight,
    AccommodationSummaryDto? Accommodation,
    TransferSummaryDto? Transfer,
    IReadOnlyList<SourceVerificationDto> Sources);

public sealed record PlanItemDto(
    Guid Id,
    PlanItemType Type,
    TransferDirection Direction,
    string Category,
    string Title,
    string? Summary,
    string Provider,
    decimal? Price,
    string? Currency,
    decimal? ConvertedPrice,
    decimal? ExchangeRate,
    string? ExchangeRateSource,
    DateTimeOffset? ExchangeRateAsOfUtc,
    bool IncludedInTotal,
    bool IsRequired,
    string? BookingUrl,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool IsExpired,
    VerificationStatus Verification,
    JsonElement Details);

public sealed record CostBreakdownLineDto(string Category, decimal Amount);

public sealed record TripPlanDetailDto(
    TripPlanSummaryDto Summary,
    DestinationDetailDto Destination,
    IReadOnlyList<CostBreakdownLineDto> CostBreakdown,
    IReadOnlyList<PlanItemDto> Items);

public sealed record OutboundLinkDto(Guid ItemId, PlanItemType Type, TransferDirection Direction, string Label, string Provider,
    string Url, VerificationStatus Verification, string Kind);
